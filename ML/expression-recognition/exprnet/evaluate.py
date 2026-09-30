"""评估（DESIGN §7）：accuracy、macro-F1、逐类 P/R/F1、混淆矩阵、ECE（15 桶，校准前后）、
OOD AUROC 与 FPR@95TPR、最终判定口径（规格 §7.2 / §11.2）、金标捏脸集逐条结果；写 metrics.json 与 report.md。

金标护栏（规格 §6.3、§10，检查逻辑在 golden_check.py）：金标的绑定名 / rig_hash 与模型训练绑定不符时报错退出，
只有 --allow-golden-skip 才降级为跳过并在报告里写明；值、标签、id 不合法一律报错。
类别集模型评金标时，标注类按类别集的 from 映射；集外类样本单独报告预测去向，不计入任何指标（规格 §15.2）。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.evaluate --run artifacts/runs/rf_synth
    python -m exprnet.evaluate --run artifacts/runs/rf_synth --golden golden/sample_rig.json --npz data/features/ckplus.npz
    python -m exprnet.evaluate --run artifacts/runs/laila_resmlp_v1 --golden golden/laila_dev.json --tag dev
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import numpy as np
import torch

from .calibrate import decide, energy_np, softmax_np
from .common import UNKNOWN_LABEL, labels_of_ckpt, load_json, make_parser, rel_to_project, resolve_cli_path, save_json
from .datasets import apply_baseline, load_features, neutral_baseline
from .golden_check import (HUMAN_STATUS, STATUS_PLACEHOLDER_ZH, GoldenError, check_entries, format_problems, load_golden,
                           normalize_expect, rig_mismatch, status_zh)
from .rig import Rig, identity_rig, rig_from_dict

REJECT_ZH = "认不出"
TAG_RX = re.compile(r"^[A-Za-z0-9_-]+$")

N_BINS = 15
DISCLAIMER_SYNTH = (
    "只用合成数据训练：验证集与训练集由同一个合成器、同一套 FACS 先验生成（同源），金标集也是按同一套先验手写的占位，"
    "**这些数字只能证明流程跑通，不能证明泛化**。正式口径要看真人捏脸盲标的金标集（DESIGN §7）。"
)
DISCLAIMER_SYNTH_HUMAN_GOLDEN = (
    "只用合成数据训练：验证集与训练集由同一个合成器、同一套 FACS 先验生成（同源），**验证集上的数字只能证明流程跑通**；"
    "泛化看下文的人工盲标金标结果（规格 §11.2）。"
)
DISCLAIMER_REAL = "训练数据含公开集（仅限非商用研究）。公开集上的准确率只说明原型能跑，发布口径看金标捏脸集（DESIGN §7）。"


# ---------------------------------------------------------------- 指标


def classification_metrics(y_true: np.ndarray, y_pred: np.ndarray, class_keys: list[str]) -> dict:
    C = len(class_keys)
    cm = np.zeros((C, C), dtype=np.int64)
    valid = y_pred >= 0
    np.add.at(cm, (y_true[valid], y_pred[valid]), 1)
    per = []
    f1s = []
    for c in range(C):
        tp = cm[c, c]
        prec = tp / cm[:, c].sum() if cm[:, c].sum() else 0.0
        support = int((y_true == c).sum())
        rec = tp / support if support else 0.0
        f1 = 2 * prec * rec / (prec + rec) if prec + rec else 0.0
        per.append({"key": class_keys[c], "precision": float(prec), "recall": float(rec), "f1": float(f1), "support": support})
        if support:
            f1s.append(f1)
    return {
        "n": int(len(y_true)),
        "accuracy": float((y_pred == y_true).mean()) if len(y_true) else float("nan"),
        "macro_f1": float(np.mean(f1s)) if f1s else float("nan"),
        "per_class": per,
        "confusion": cm.tolist(),
    }


def open_set_metrics(y_true: np.ndarray, y_pred: np.ndarray, class_keys: list[str]) -> dict:
    """最终判定口径（规格 §7.2、§11.2）。y_true = -1 表示 unknown 真值；y_pred = -1 表示判「认不出」。

    混淆矩阵 (C+1)×(C+1)：行 = 各类 + unknown 真值行，列 = 各类 + 「认不出」列。
    召回的分母是该类全部真值样本（被拒识计为漏判）；precision 的分母是被判成该类的全部样本（含 unknown 真值被误判成它）；
    已知类 macro-F1 只对有样本的类取平均；unknown 拒识率只在 unknown 真值样本上算。
    """
    C = len(class_keys)
    y_true = np.asarray(y_true, dtype=np.int64)
    y_pred = np.asarray(y_pred, dtype=np.int64)
    cm = np.zeros((C + 1, C + 1), dtype=np.int64)
    for t, p in zip(y_true, y_pred):
        cm[C if t < 0 else t, C if p < 0 else p] += 1
    per, f1s = [], []
    for c in range(C):
        tp, sup, pred_n = int(cm[c, c]), int(cm[c].sum()), int(cm[:, c].sum())
        prec = tp / pred_n if pred_n else 0.0
        rec = tp / sup if sup else 0.0
        f1 = 2 * prec * rec / (prec + rec) if prec + rec else 0.0
        per.append({"key": class_keys[c], "support": sup, "recall": float(rec), "precision": float(prec), "f1": float(f1),
                    "reject_rate": float(cm[c, C] / sup) if sup else None})
        if sup:
            f1s.append(f1)
    known = y_true >= 0
    n_known, n_unk = int(known.sum()), int((~known).sum())
    return {
        "n_known": n_known,
        "n_unknown": n_unk,
        "known_accuracy": float((y_pred[known] == y_true[known]).mean()) if n_known else None,
        "known_macro_f1": float(np.mean(f1s)) if f1s else None,
        "known_reject_rate": float((y_pred[known] < 0).mean()) if n_known else None,
        "unknown_reject_rate": float((y_pred[~known] < 0).mean()) if n_unk else None,
        "per_class": per,
        "confusion": cm.tolist(),
        "rows": list(class_keys) + [UNKNOWN_LABEL],
        "cols": list(class_keys) + [REJECT_ZH],
    }


def ece_score(probs: np.ndarray, y: np.ndarray, n_bins: int = N_BINS) -> float:
    conf = probs.max(-1)
    correct = probs.argmax(-1) == y
    edges = np.linspace(0, 1, n_bins + 1)
    ece = 0.0
    for lo, hi in zip(edges[:-1], edges[1:]):
        m = (conf > lo) & (conf <= hi)
        if m.any():
            ece += m.mean() * abs(correct[m].mean() - conf[m].mean())
    return float(ece)


def nll(probs: np.ndarray, y: np.ndarray) -> float:
    return float(-np.log(np.clip(probs[np.arange(len(y)), y], 1e-12, 1)).mean())


def auroc(score_id: np.ndarray, score_ood: np.ndarray) -> float:
    """OOD 为正类、分数越大越像 OOD 时的 AUROC（Mann–Whitney U，并列取平均秩）。"""
    s = np.concatenate([score_id, score_ood])
    order = s.argsort(kind="mergesort")
    ranks = np.empty(len(s), dtype=np.float64)
    sorted_s = s[order]
    i = 0
    while i < len(s):
        j = i
        while j + 1 < len(s) and sorted_s[j + 1] == sorted_s[i]:
            j += 1
        ranks[order[i : j + 1]] = (i + j) / 2.0 + 1
        i = j + 1
    n_id, n_ood = len(score_id), len(score_ood)
    r_ood = ranks[n_id:].sum()
    return float((r_ood - n_ood * (n_ood + 1) / 2) / (n_id * n_ood))


def fpr_at_tpr(score_id: np.ndarray, score_ood: np.ndarray, tpr: float = 0.95) -> float:
    """分布内样本 tpr 比例通过（score ≤ 阈值）时，OOD 样本被误放行的比例。"""
    thr = np.quantile(score_id, tpr)
    return float((score_ood <= thr).mean())


# ---------------------------------------------------------------- 推理


@torch.no_grad()
def logits_of(model, x: np.ndarray, mask: np.ndarray, batch: int = 4096) -> np.ndarray:
    model.eval()
    out = []
    for s in range(0, len(x), batch):
        xb = torch.from_numpy((x[s : s + batch] * mask).astype(np.float32))
        out.append(model(xb).numpy())
    return np.concatenate(out) if out else np.zeros((0, 0), np.float32)


def rig_of_ckpt(ckpt: dict, canonical) -> Rig:
    r = ckpt.get("train_rig")
    if r is None or r.get("identity"):
        return identity_rig(canonical)
    return rig_from_dict(r, canonical)


# ---------------------------------------------------------------- 金标


RULE_ZH = {"tnr95": "TNR95（留出负样本 95% 被拒）", "id95": "ID95（分布内验证集 95% 通过，旧规则）", "current": "当前阈值"}


def thresholds_of(ckpt: dict) -> dict[str, float]:
    """当前规则在前，其余规则的阈值作对照；旧产物没记录规则时只有 current。"""
    rule = ckpt.get("threshold_rule")
    out: dict[str, float] = {}
    if rule:
        out[rule] = float(ckpt["energy_threshold"])
    for r in ("tnr95", "id95"):
        v = ckpt.get(f"energy_threshold_{r}")
        if v is not None and r not in out:
            out[r] = float(v)
    return out or {"current": float(ckpt["energy_threshold"])}


def evaluate_golden(golden_path: Path, rig: Rig, classifier, canonical, ckpt: dict,
                    thresholds: dict[str, float] | None = None, allow_skip: bool = False) -> dict:
    """金标捏脸集：每条是一组滑杆值 + 期望标签（标注类或 unknown）。走完整部署图（与 ONNX 同一套公式）。
    逐条结果按当前阈值判定；thresholds 里的每个阈值另算一份通过率摘要（对照用）。

    护栏：绑定名 / rig_hash 不符时抛 GoldenError（allow_skip=True 时返回带 skipped 的结果）；
    值、标签、id 不合法一律抛 GoldenError。类别集模型：expect 按 from 映射，集外类不计入任何指标，另报预测去向。
    """
    from .export import DeployModel

    file = rel_to_project(golden_path)
    g = load_golden(golden_path)
    mm = rig_mismatch(g, rig, file)
    if mm:
        if allow_skip:
            return {"file": file, "rig": g.get("rig"), "model_rig": rig.name,
                    "skipped": f"{mm}；已按 --allow-golden-skip 降级为跳过，本次没有金标结果"}
        raise GoldenError(f"{mm}。金标只能评同一绑定训练的模型；确实要跳过请加 --allow-golden-skip（报告里会写明跳过）")
    labels = labels_of_ckpt(ckpt)
    problems = check_entries(g, rig, labels.annotation_keys, file)
    if problems:
        raise GoldenError(f"金标 {file} 有 {len(problems)} 个问题，不能用于评估：\n{format_problems(problems)}")
    keys = ckpt["class_keys"]
    rows, expect, targets, ids, notes = [], [], [], [], []
    for e in g["entries"]:
        v = rig.defaults.copy()
        for k, val in e["sliders"].items():
            v[rig.keys.index(k)] = val
        rows.append(v)
        expect.append(e["expect"])
        targets.append(labels.map_annotation(normalize_expect(e["expect"])))  # 集外类为 None
        ids.append(e["id"])
        notes.append(e.get("note", ""))
    deploy = DeployModel(rig, canonical, classifier, ckpt["temperature"]).eval()
    with torch.no_grad():
        p, en = deploy(torch.from_numpy(np.array(rows, dtype=np.float32)))
    p, en = p.numpy(), en.numpy()
    dec = decide(p, en, ckpt["energy_threshold"], ckpt["min_confidence"])
    entries = []
    n_ok = 0
    per_cls: dict[str, list[int]] = {}
    for i in range(len(rows)):
        pred = UNKNOWN_LABEL if dec[i] < 0 else keys[dec[i]]
        top = int(p[i].argmax())
        ent = {"id": ids[i], "expect": expect[i], "pred": pred, "ok": None,
               "top1": keys[top], "top1_prob": float(p[i, top]), "energy": float(en[i]), "note": notes[i]}
        if targets[i] is None:
            ent["out_of_set"] = True  # 集外类：只报告去向，不计入任何指标
        else:
            ok = pred == targets[i]
            ent["ok"] = bool(ok)
            n_ok += ok
            per_cls.setdefault(expect[i], [0, 0])
            per_cls[expect[i]][0] += ok
            per_cls[expect[i]][1] += 1
            if targets[i] != expect[i]:
                ent["target"] = targets[i]
        entries.append(ent)
    tgt = np.array([t if t is not None else "" for t in targets])
    scored = np.array([t is not None for t in targets], dtype=bool)
    is_unk = tgt == UNKNOWN_LABEL
    is_known = scored & ~is_unk
    known = [e for e, k in zip(entries, is_known) if k]
    unk = [e for e, u in zip(entries, is_unk) if u]
    by_thr = {}
    for name, t in (thresholds or {}).items():
        d = decide(p, en, t, ckpt["min_confidence"])
        pred_t = np.array([UNKNOWN_LABEL if k < 0 else keys[k] for k in d])
        by_thr[name] = {
            "threshold": float(t),
            "known_pass": int((d[is_known] >= 0).sum()),
            "known_correct": int((pred_t[is_known] == tgt[is_known]).sum()),
            "n_known": int(is_known.sum()),
            "weird_reject": int((d[is_unk] < 0).sum()),
            "n_weird": int(is_unk.sum()),
            "known_pass_energy_only": int((en[is_known] <= t).sum()),
        }
    y_true = np.array([keys.index(t) if t != UNKNOWN_LABEL else -1 for t in tgt[scored]], dtype=np.int64)
    n_scored = int(scored.sum())
    out = {
        "file": file,
        "rig": rig.name,
        "status": g.get("status"),
        "status_zh": status_zh(g.get("status")),
        "human_labeled": g.get("status") == HUMAN_STATUS,
        "n": len(entries),
        "n_known": len(known),
        "n_weird": len(unk),
        "known_correct": int(sum(e["ok"] for e in known)),
        "weird_correct": int(sum(e["ok"] for e in unk)),
        "by_threshold": by_thr,
        "accuracy": n_ok / n_scored if n_scored else float("nan"),
        "known_accuracy": float(np.mean([e["ok"] for e in known])) if known else None,
        "unknown_detection": float(np.mean([e["ok"] for e in unk])) if unk else None,
        "per_expect": {k: {"ok": v[0], "n": v[1]} for k, v in per_cls.items()},
        "final": open_set_metrics(y_true, dec[scored], keys),
        "entries": entries,
    }
    if labels.label_set:
        dest: dict[str, dict[str, int]] = {}
        for e in entries:
            if e.get("out_of_set"):
                dest.setdefault(e["expect"], {})
                dest[e["expect"]][e["pred"]] = dest[e["expect"]].get(e["pred"], 0) + 1
        out["label_set"] = labels.label_set["name"]
        out["out_of_set"] = {"classes": labels.dropped, "n": int((~scored).sum()), "destinations": dest,
                             "note": "集外类样本只报告预测去向，不计入任何指标（规格 §15.2）"}
    return out


# ---------------------------------------------------------------- 整体评估


def output_names(tag: str | None) -> tuple[str, str]:
    """评估产物文件名：不给 tag 时是 metrics.json / report.md；给了是 metrics_<tag>.json / report_<tag>.md。"""
    if tag is None:
        return "metrics.json", "report.md"
    if not TAG_RX.match(tag):
        raise ValueError(f"--tag 只能用字母、数字、下划线、连字符：{tag!r}")
    return f"metrics_{tag}.json", f"report_{tag}.md"


def evaluate_run(run_dir: Path, golden: Path | None = None, npz: list[Path] | None = None, write: bool = True,
                 extra: dict | None = None, log=print, allow_golden_skip: bool = False, tag: str | None = None) -> dict:
    from .models import count_params, load_checkpoint

    metrics_name, report_name = output_names(tag)
    run_dir = Path(run_dir)
    classifier, ckpt, canonical = load_checkpoint(run_dir)
    keys = ckpt["class_keys"]
    T = float(ckpt["temperature"])
    thr = float(ckpt["energy_threshold"])
    mask = canonical.mask_vector
    ev = np.load(run_dir / "eval_data.npz")
    y_val = ev["y_val"]

    # 部署视角：有绑定时验证集取绑定投影版
    lz = logits_of(classifier, ev["xp_val"], mask)
    lz_raw = logits_of(classifier, ev["x_val"], mask)
    p_before = softmax_np(lz)
    p_after = softmax_np(lz / T)
    val = classification_metrics(y_val, p_after.argmax(-1), keys)
    val["view"] = "绑定投影后（部署视角）" if ckpt.get("train_rig") and not ckpt["train_rig"].get("identity") else "原值（单位绑定）"
    val_raw = classification_metrics(y_val, lz_raw.argmax(-1), keys)
    by_src = {}
    for si, src in enumerate(ckpt.get("data_sources", [])):
        m = ev["src_val"] == si
        if m.any():
            r = classification_metrics(y_val[m], p_after[m].argmax(-1), keys)
            by_src[src["name"]] = {"n": r["n"], "accuracy": r["accuracy"], "macro_f1": r["macro_f1"]}

    calib = {"temperature": T, "ece_before": ece_score(p_before, y_val), "ece_after": ece_score(p_after, y_val),
             "nll_before": nll(p_before, y_val), "nll_after": nll(p_after, y_val), "n_bins": N_BINS}

    e_id = energy_np(lz, T)
    e_ood = energy_np(logits_of(classifier, ev["negp_heldout"], mask), T)
    msp_ood = softmax_np(logits_of(classifier, ev["negp_heldout"], mask) / T).max(-1)
    ood = {
        "score": "energy",
        "auroc": auroc(e_id, e_ood),
        "fpr95": fpr_at_tpr(e_id, e_ood, 0.95),
        "auroc_msp": auroc(-p_after.max(-1), -msp_ood),
        "energy_threshold": thr,
        "id_pass_rate": float((e_id <= thr).mean()),
        "ood_reject_rate": float((e_ood > thr).mean()),
        "n_id": int(len(e_id)),
        "n_ood": int(len(e_ood)),
    }
    dec_val = decide(p_after, e_id, thr, ckpt["min_confidence"])
    ood["id_unknown_rate_with_min_conf"] = float((dec_val < 0).mean())
    val_final = {"view": val["view"], **open_set_metrics(y_val, dec_val, keys),
                 "note": "按当前阈值最终判定（规格 §7.2）：被拒识计为该类漏判；合成验证集没有 unknown 真值行，"
                         "怪脸拒识率见「认不出」一节的留出集拒识率"}

    # 各阈值规则的对照（DESIGN §6）：分布内通过率、留出负样本拒识率
    p_ood = softmax_np(logits_of(classifier, ev["negp_heldout"], mask) / T)
    has_calib = "negp_calib" in ev.files
    if has_calib:
        e_cal = energy_np(logits_of(classifier, ev["negp_calib"], mask), T)
    thr_all = thresholds_of(ckpt)
    by_rule = {}
    for name, t in thr_all.items():
        d_id = decide(p_after, e_id, t, ckpt["min_confidence"])
        d_ood = decide(p_ood, e_ood, t, ckpt["min_confidence"])
        by_rule[name] = {
            "threshold": t,
            "id_pass_rate": float((e_id <= t).mean()),
            "id_pass_rate_with_min_conf": float((d_id >= 0).mean()),
            "id_correct_and_pass_rate": float((d_id == y_val).mean()),
            "heldout_neg_reject_rate": float((e_ood > t).mean()),
            "heldout_neg_reject_rate_with_min_conf": float((d_ood < 0).mean()),
        }
        if has_calib:
            by_rule[name]["calib_neg_reject_rate"] = float((e_cal > t).mean())
    neg = ckpt.get("negatives") or {}
    thresholds = {
        "rule": ckpt.get("threshold_rule", "current"), "min_confidence": float(ckpt["min_confidence"]),
        "params": ckpt.get("threshold_params"), "by_rule": by_rule,
        "neg_sets": {
            "calib": {**neg.get("calib", {}), "desc": "标定集：TNR95 阈值只在这批上取，calib_neg_reject_rate 按构造约为 95%，不当验证用"},
            "heldout": {**neg.get("heldout", {}), "desc": "留出集：不参与训练与定阈值，heldout_neg_reject_rate* 与 AUROC / FPR95 都在这批上算"},
        },
        "note": ("TNR95 阈值取自标定集（与留出集种子不同、样本不重叠）；留出集拒识率是独立检验" if has_calib else
                 "旧产物没有标定集：TNR95 阈值取自留出集，留出集拒识率按构造约为 95%"),
    }

    metrics = {
        "run": run_dir.name,
        "model": ckpt["model_name"],
        "params": count_params(classifier),
        "commercial_use_allowed": bool(ckpt.get("commercial_use_allowed", False)),
        "data_sources": ckpt.get("data_sources", []),
        "val": val,
        "val_raw": {"view": "原值（未投影）", "accuracy": val_raw["accuracy"], "macro_f1": val_raw["macro_f1"]},
        "val_by_source": by_src,
        "val_final": val_final,
        "calibration": calib,
        "ood": ood,
        "thresholds": thresholds,
        "golden": None,
        "external": {},
    }

    rig = rig_of_ckpt(ckpt, canonical)
    if golden is not None:  # 文件缺失、绑定不符、数据不合法都抛 GoldenError，不再静默跳过
        metrics["golden"] = evaluate_golden(Path(golden), rig, classifier, canonical, ckpt, thr_all,
                                            allow_skip=allow_golden_skip)
        if "skipped" in metrics["golden"]:
            log(f"[评估] 金标已跳过：{metrics['golden']['skipped']}")
        for name, st in (metrics["golden"].get("by_threshold") or {}).items():
            by_rule[name].update({"golden_known_pass": st["known_pass"], "golden_known_correct": st["known_correct"],
                                  "golden_n_known": st["n_known"], "golden_weird_reject": st["weird_reject"],
                                  "golden_n_weird": st["n_weird"]})

    labels = labels_of_ckpt(ckpt)  # 类别集模型：公开集的标注类按 from 映射，集外类丢弃
    for p in npz or []:
        fs = load_features(p, canonical)
        idx = [labels.to_class_index(l) if l else None for l in fs.label]
        keep = np.array([i is not None for i in idx], dtype=bool)  # 只评训练时启用的类别
        y = np.array([i for i in idx if i is not None], dtype=np.int64)
        X = fs.x[keep]
        test = fs.split[keep] == "test"
        use = test if test.any() else np.ones(len(X), bool)
        neutral_idx = keys.index("neutral") if "neutral" in keys else None
        base_mask = (fs.split[keep] == "train") if (fs.split[keep] == "train").any() else use
        b = neutral_baseline(X[base_mask], y[base_mask], neutral_idx)
        Xn = apply_baseline(X[use], b)
        from .datasets import project_cached

        Xp = project_cached(rig, Xn, mask)
        lz_e = logits_of(classifier, Xp, mask)
        r = classification_metrics(y[use], (lz_e / T).argmax(-1), keys)
        r["ece_after"] = ece_score(softmax_np(lz_e / T), y[use])
        r["split_used"] = "test" if test.any() else "全部"
        metrics["external"][fs.dataset] = r
        log(f"[评估] 外部集 {fs.dataset}：acc {r['accuracy']:.4f}，macro-F1 {r['macro_f1']:.4f}（{r['split_used']}，n={r['n']}）")

    if tag is not None:
        metrics["eval_tag"] = tag
    if extra:
        metrics.update(extra)
    if write:
        save_json(metrics, run_dir / metrics_name)
        (run_dir / report_name).write_text(render_report(metrics, ckpt), encoding="utf-8", newline="\n")
    return metrics


# ---------------------------------------------------------------- 报告


def _f(v, nd=4):
    return "—" if v is None or (isinstance(v, float) and np.isnan(v)) else f"{v:.{nd}f}"


def _confusion_lines(fin: dict, zh: dict) -> list[str]:
    """最终判定混淆矩阵：行 = 各类（+ unknown 真值行，有样本时），列 = 各类 + 「认不出」。"""
    rows, cols, cm = fin["rows"], fin["cols"], fin["confusion"]
    L = ["| 真值 \\ 判定 | " + " | ".join(cols) + " |", "| --- | " + " | ".join("---" for _ in cols) + " |"]
    for i, (k, row) in enumerate(zip(rows, cm)):
        if i == len(rows) - 1 and not fin["n_unknown"]:
            continue
        L.append(f"| {k if k == UNKNOWN_LABEL else f'{k}（{zh.get(k, k)}）'} | " + " | ".join(str(x) for x in row) + " |")
    return L


def _per_class_lines(fin: dict, zh: dict) -> list[str]:
    L = ["| 类别 | 样本数 | 召回（被拒计漏判） | 精确率 | F1 | 被拒比例 |", "| --- | --- | --- | --- | --- | --- |"]
    for r in fin["per_class"]:
        L.append(f"| {r['key']}（{zh.get(r['key'], r['key'])}） | {r['support']} | {_f(r['recall'], 3)} | {_f(r['precision'], 3)} | "
                 f"{_f(r['f1'], 3)} | {_f(r['reject_rate'], 3)} |")
    return L


def render_report(m: dict, ckpt: dict) -> str:
    keys = ckpt["class_keys"]
    zh = dict(zip(ckpt["class_keys"], ckpt["class_zh"]))
    g = m.get("golden")
    human_golden = bool(g and g.get("human_labeled"))
    L = []
    L.append(f"# 训练报告：{m['run']}" + (f"（评估标签 {m['eval_tag']}）" if m.get("eval_tag") else "") + "\n")
    if m["commercial_use_allowed"]:
        L.append(f"> {DISCLAIMER_SYNTH_HUMAN_GOLDEN if human_golden else DISCLAIMER_SYNTH}\n")
    else:
        L.append(f"> {DISCLAIMER_REAL}\n")
    L.append("## 概况\n")
    L.append(f"- 模型：`{m['model']}`，参数量 {m['params']:,}")
    L.append(f"- 类别：{', '.join(f'{k}（{zh[k]}）' for k in keys)}")
    ls = ckpt.get("label_set")
    if ls:
        L.append(f"- 类别集：`{ls['name']}`（{ls.get('file') or '—'}）："
                 + "，".join(f"{c['key']} ← {'+'.join(c['from'])}" for c in ls["classes"])
                 + (f"；集外类 {', '.join(ls['drop'])}（不进正样本，评估时只报告去向）" if ls.get("drop") else ""))
    rig = ckpt.get("train_rig") or {}
    L.append(f"- 训练绑定：{rig.get('name', 'identity')}，哈希 `{str(ckpt.get('train_rig_hash'))[:16]}…`")
    vf = ckpt.get("variant_filter") or {}
    if vf.get("enabled"):
        L.append(f"- 按绑定剔除变体：已打开，剔除 {len(vf.get('dropped', []))} 个（见下文「训练目标剔除的变体」）")
    L.append(f"- 可商用：{'是（只用合成数据）' if m['commercial_use_allowed'] else '否（含公开集）'}")
    L.append("- 数据来源：")
    for s in m["data_sources"]:
        L.append(f"  - {s['name']}（{s['kind']}）：训练 {s['n_train']}，验证 {s['n_val']}，划分 {s.get('split')}；授权：{s['license']}")
    if "training" in m:
        t = m["training"]
        L.append(f"- 训练：跑了 {t['epochs_run']} 个 epoch，最佳 epoch {t['best_epoch']}（验证 macro-F1 {_f(t['best_val_macro_f1'])}），用时 {t['seconds']:.0f} 秒")
    L.append("")
    if vf.get("enabled"):
        L.append("## 训练目标剔除的变体（按绑定，规格 §15.2）\n")
        L.append(f"- 规则：变体的核心 AU（aus + 所选备选；optional 不加）按统一强度 {vf['probe_intensity']:g} 摆出 → "
                 f"投影到训练绑定 {vf['rig']}（哈希 `{vf['rig_hash'][:16]}…`）→ 相对残差 ≥ {vf['rel_residual_threshold']:g} 就不进训练目标；"
                 "带 choose 的变体逐个备选判断，只剔除做不出来的那个备选（剩下的备选平分，变体权重按保留比例缩小）。")
        if vf.get("note"):
            L.append(f"- {vf['note']}")
        L.append(f"- 检查了 {vf['n_checked']} 个变体 / 备选，剔除 {len(vf['dropped'])} 个。\n")
        L.append("| 类别 | 标注类 | 变体 # | 备选 # | 出处 | 核心 AU | 相对残差 | 剔除 | 说明 |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |")
        dropped = {(r["emotion"], r["variant"], r["choose"]) for r in vf["dropped"]}
        for r in vf["variants"]:
            mark = "**剔除**" if (r["emotion"], r["variant"], r["choose"]) in dropped else ""
            L.append(f"| {r['class']} | {r['emotion']} | {r['variant']} | {'—' if r['choose'] is None else r['choose']} | {r['source']} | "
                     f"{'+'.join(r['aus']) or '（无，不判）'} | {_f(r['rel_residual'], 3)} | {mark} | {r.get('note', '').replace('|', '&#124;')} |")
        L.append("")
    v = m["val"]
    L.append(f"## 验证集（{v['view']}，n={v['n']}）\n")
    L.append(f"- accuracy **{_f(v['accuracy'])}**，macro-F1 **{_f(v['macro_f1'])}**")
    L.append(f"- 原值视角（未投影）：accuracy {_f(m['val_raw']['accuracy'])}，macro-F1 {_f(m['val_raw']['macro_f1'])}")
    for name, r in m.get("val_by_source", {}).items():
        L.append(f"- 来源 {name}：n={r['n']}，accuracy {_f(r['accuracy'])}，macro-F1 {_f(r['macro_f1'])}")
    L.append("\n| 类别 | P | R | F1 | 样本数 |\n| --- | --- | --- | --- | --- |")
    for r in v["per_class"]:
        L.append(f"| {r['key']}（{zh[r['key']]}） | {_f(r['precision'], 3)} | {_f(r['recall'], 3)} | {_f(r['f1'], 3)} | {r['support']} |")
    L.append("\n混淆矩阵（行 = 真值，列 = 预测）：\n")
    L.append("| 真值 \\ 预测 | " + " | ".join(keys) + " |")
    L.append("| --- | " + " | ".join("---" for _ in keys) + " |")
    for k, row in zip(keys, v["confusion"]):
        L.append(f"| {k} | " + " | ".join(str(x) for x in row) + " |")
    vfin = m.get("val_final")
    if vfin:
        L.append("\n### 最终判定口径（规格 §7.2 / §11.2）\n")
        L.append(f"- 最终判对率 **{_f(vfin['known_accuracy'])}**，最终 macro-F1 **{_f(vfin['known_macro_f1'])}**，"
                 f"已知类被拒识比例 **{_f(vfin['known_reject_rate'])}**")
        L.append(f"- 注：{vfin['note']}\n")
        L += _per_class_lines(vfin, zh)
        L.append("")
        L += _confusion_lines(vfin, zh)
    c = m["calibration"]
    L.append("\n## 校准\n")
    L.append(f"- 温度 T = {_f(c['temperature'])}")
    L.append(f"- ECE（{c['n_bins']} 桶）：校准前 {_f(c['ece_before'])} → 校准后 {_f(c['ece_after'])}")
    L.append(f"- NLL：校准前 {_f(c['nll_before'])} → 校准后 {_f(c['nll_after'])}")
    o = m["ood"]
    L.append("\n## 「认不出」（OOD：验证集 vs 留出负样本）\n")
    L.append(f"- energy AUROC **{_f(o['auroc'])}**，FPR@95TPR **{_f(o['fpr95'])}**（对照：最大 softmax 概率 AUROC {_f(o['auroc_msp'])}）")
    th = m.get("thresholds")
    if th:
        L.append(f"- 阈值规则：**{RULE_ZH.get(th['rule'], th['rule'])}**，energy_threshold = **{_f(o['energy_threshold'])}**；"
                 f"min_confidence = {_f(th['min_confidence'], 2)}（游戏侧 energy > 阈值 或 max(probs) < min_confidence 判「认不出」）")
        L.append(f"- 注：{th['note']}\n")
        for k in ("calib", "heldout"):
            ns = th.get("neg_sets", {}).get(k)
            if ns:
                L.append(f"- {ns['desc']}（{ns.get('n', '—')} 个，种子 {ns.get('seed', '—')}）")
        L.append("")
        L.append("| 规则 | 阈值 | 验证集通过率（仅 energy） | 验证集通过率（energy + min_conf） | 验证集判对且通过 "
                 "| 标定集拒识率（仅 energy） | 留出集拒识率（仅 energy） | 留出集拒识率（energy + min_conf） | 金标已知类通过 | 金标已知类判对 | 金标怪脸被拒 |")
        L.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
        for name, r in th["by_rule"].items():
            cur = "（当前）" if name == th["rule"] else "（对照）"
            n_k, n_w = r.get("golden_n_known"), r.get("golden_n_weird")
            gk = f"{r['golden_known_pass']}/{n_k}" if n_k else "—"
            gc = f"{r['golden_known_correct']}/{n_k}" if n_k else "—"
            gw = f"{r['golden_weird_reject']}/{n_w}" if n_w else "—"
            L.append(f"| {RULE_ZH.get(name, name)}{cur} | {_f(r['threshold'])} | {_f(r['id_pass_rate'])} | "
                     f"{_f(r['id_pass_rate_with_min_conf'])} | {_f(r['id_correct_and_pass_rate'])} | "
                     f"{_f(r.get('calib_neg_reject_rate'))} | {_f(r['heldout_neg_reject_rate'])} | {_f(r['heldout_neg_reject_rate_with_min_conf'])} | {gk} | {gc} | {gw} |")
    else:
        L.append(f"- energy_threshold = {_f(o['energy_threshold'])}：验证集通过率 {_f(o['id_pass_rate'])}，留出负样本拒识率 {_f(o['ood_reject_rate'])}")
        L.append(f"- 加上 min_confidence = {_f(ckpt['min_confidence'], 2)} 后，验证集被判「认不出」的比例 {_f(o['id_unknown_rate_with_min_conf'])}")
    L.append("\n## 金标捏脸集\n")
    if not g:
        L.append("- 未评估（没有提供金标文件）")
    elif "skipped" in g:
        L.append(f"- **已跳过**：{g['skipped']}")
    else:
        n_oos = (g.get("out_of_set") or {}).get("n", 0)
        L.append(f"- 文件 `{g['file']}`（绑定 {g['rig']}）；状态：**{g.get('status_zh', STATUS_PLACEHOLDER_ZH)}**"
                 f"（文件 status：{g.get('status')}）")
        L.append(f"- 共 {g['n']} 条：总准确率 **{_f(g['accuracy'])}**，已知类 {g['known_correct']}/{g['n_known']}，"
                 f"怪脸 {g['weird_correct']}/{g['n_weird']}（按当前阈值）"
                 + (f"；集外类 {n_oos} 条单独报告，不计入任何指标" if n_oos else ""))
        if not g.get("human_labeled"):
            L.append("- 注意：这是按 FACS 原型手写的占位金标，与合成器同源，不能证明泛化（DESIGN §7）")
        fin = g.get("final")
        if fin:
            L.append("\n### 最终判定口径（规格 §11.2）\n")
            L.append(f"- 已知类最终判对率 **{_f(fin['known_accuracy'])}**（被拒识也计为错误），已知类 macro-F1 **{_f(fin['known_macro_f1'])}**，"
                     f"已知类被拒识比例 **{_f(fin['known_reject_rate'])}**；unknown 拒识率 **{_f(fin['unknown_reject_rate'])}**"
                     f"（{fin['n_known']} 个已知类样本，{fin['n_unknown']} 个 unknown 样本）")
            L.append("- 召回分母是该类全部样本（被拒识计为漏判）；精确率分母是被判成该类的全部样本（含 unknown 被误判成它）；"
                     "macro-F1 只对有样本的类取平均\n")
            L += _per_class_lines(fin, zh)
            L.append("\n混淆矩阵（行 = 真值，含 unknown 真值行；列 = 判定，含「认不出」列）：\n")
            L += _confusion_lines(fin, zh)
        oos = g.get("out_of_set")
        if oos and oos["n"]:
            L.append(f"\n### 集外类的预测去向（{', '.join(oos['classes'])}；不计入任何指标，规格 §15.2）\n")
            L.append("| 标注类 | 条数 | 去向 |\n| --- | --- | --- |")
            for k, d in oos["destinations"].items():
                L.append(f"| {k} | {sum(d.values())} | " + "，".join(f"{p} {n}" for p, n in sorted(d.items(), key=lambda kv: -kv[1])) + " |")
        L.append("\n| id | 期望 | 判定 | 对 | top1 | top1 概率 | energy | 备注 |\n| --- | --- | --- | --- | --- | --- | --- | --- |")
        for e in g["entries"]:
            exp = e["expect"] + (f" → {e['target']}" if e.get("target") else "") + ("（集外）" if e.get("out_of_set") else "")
            ok = "—" if e["ok"] is None else ("✓" if e["ok"] else "✗")
            L.append(f"| {e['id']} | {exp} | {e['pred']} | {ok} | {e['top1']} | {_f(e['top1_prob'], 3)} | {_f(e['energy'], 2)} | {e['note']} |")
    if m.get("external"):
        L.append("\n## 外部数据集\n")
        for name, r in m["external"].items():
            L.append(f"- {name}（{r['split_used']}，n={r['n']}）：accuracy {_f(r['accuracy'])}，macro-F1 {_f(r['macro_f1'])}，ECE {_f(r['ece_after'])}")
    if "training" in m and m["training"].get("history"):
        L.append("\n## 训练曲线\n")
        L.append("| epoch | 训练损失 | CE | OE 超出 log C | 验证 acc | 验证 macro-F1 | lr |\n| --- | --- | --- | --- | --- | --- | --- |")
        for h in m["training"]["history"]:
            L.append(f"| {h['epoch']} | {_f(h['loss'])} | {_f(h['ce'])} | {_f(h['oe'])} | {_f(h['val_acc'])} | {_f(h['val_macro_f1'])} | {h['lr']:.2e} |")
    L.append("")
    return "\n".join(L)


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.evaluate",
                     "重新评估一个训练产物，重写其中的 metrics.json 与 report.md（给 --tag 时写 metrics_<tag>.json / report_<tag>.md）。")
    ap.add_argument("--run", required=True, help="训练产物目录，如 artifacts/runs/rf_synth")
    ap.add_argument("--golden", default=None, help="金标集 json（默认用训练配置里的 golden）；写 none 表示不评金标")
    ap.add_argument("--allow-golden-skip", action="store_true",
                    help="金标的绑定名 / rig_hash 与模型训练绑定不符时降级为跳过（默认报错退出），报告里会写明跳过")
    ap.add_argument("--tag", default=None,
                    help="评估标签：报告写到 metrics_<tag>.json / report_<tag>.md，开发集与测试集的评估互不覆盖（只能用字母、数字、_、-）")
    ap.add_argument("--npz", nargs="*", default=[], help="额外评估的特征缓存 npz（有官方 test 划分时只评 test）")
    args = ap.parse_args(argv)
    run_dir = resolve_cli_path(args.run)
    try:
        metrics_name, report_name = output_names(args.tag)
    except ValueError as e:
        ap.error(str(e))
    old = load_json(run_dir / "metrics.json") if (run_dir / "metrics.json").exists() else {}
    if args.golden is not None and args.golden.lower() == "none":
        golden = None
    elif args.golden:
        golden = resolve_cli_path(args.golden)
    else:
        golden = None
        import yaml

        snap = run_dir / "config.yaml"
        if snap.exists():
            from .common import resolve_path

            g = (yaml.safe_load(snap.read_text(encoding="utf-8")) or {}).get("golden")
            golden = resolve_path(g) if g else None
    extra = {"training": old["training"]} if "training" in old else None
    try:
        m = evaluate_run(run_dir, golden=golden, npz=[resolve_cli_path(p) for p in args.npz], extra=extra,
                         allow_golden_skip=args.allow_golden_skip, tag=args.tag)
    except GoldenError as e:
        print(f"[错误] {e}", file=sys.stderr)
        print("[错误] 评估中止，没有写任何报告", file=sys.stderr)
        return 2
    print(f"[评估] 验证集 acc {m['val']['accuracy']:.4f}，macro-F1 {m['val']['macro_f1']:.4f}；"
          f"ECE {m['calibration']['ece_before']:.4f} → {m['calibration']['ece_after']:.4f}；"
          f"OOD AUROC {m['ood']['auroc']:.4f}，FPR95 {m['ood']['fpr95']:.4f}")
    vf = m["val_final"]
    print(f"[评估] 验证集最终判定：判对率 {vf['known_accuracy']:.4f}，macro-F1 {vf['known_macro_f1']:.4f}，"
          f"已知类被拒 {vf['known_reject_rate']:.4f}")
    g = m.get("golden")
    if g and "accuracy" in g:
        print(f"[评估] 金标（{g['status_zh']}）准确率 {g['accuracy']:.4f}（{g['n']} 条；已知类 {g['known_correct']}/{g['n_known']}，"
              f"怪脸 {g['weird_correct']}/{g['n_weird']}）")
    print(f"[评估] 报告：{rel_to_project(run_dir / report_name)}、{rel_to_project(run_dir / metrics_name)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
