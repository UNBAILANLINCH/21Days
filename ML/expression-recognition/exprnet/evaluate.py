"""评估（DESIGN §7）：accuracy、macro-F1、逐类 P/R/F1、混淆矩阵、ECE（15 桶，校准前后）、
OOD AUROC 与 FPR@95TPR、金标捏脸集逐条结果；写 metrics.json 与 report.md。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.evaluate --run artifacts/runs/rf_synth
    python -m exprnet.evaluate --run artifacts/runs/rf_synth --golden golden/sample_rig.json --npz data/features/ckplus.npz
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import torch

from .calibrate import decide, energy_np, softmax_np
from .common import make_parser, load_json, load_labels, rel_to_project, resolve_cli_path, save_json
from .datasets import apply_baseline, load_features, neutral_baseline
from .rig import Rig, identity_rig, rig_from_dict

N_BINS = 15
DISCLAIMER_SYNTH = (
    "只用合成数据训练：验证集与训练集由同一个合成器、同一套 FACS 先验生成（同源），金标集也是按同一套先验手写的占位，"
    "**这些数字只能证明流程跑通，不能证明泛化**。正式口径要看真人捏脸盲标的金标集（DESIGN §7）。"
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
                    thresholds: dict[str, float] | None = None) -> dict | None:
    """金标捏脸集：每条是一组滑杆值 + 期望标签（或 unknown）。走完整部署图（与 ONNX 同一套公式）。
    逐条结果按当前阈值判定；thresholds 里的每个阈值另算一份通过率摘要（对照用）。"""
    from .export import DeployModel

    g = load_json(golden_path)
    if g.get("rig") != rig.name:
        return {"file": rel_to_project(golden_path), "skipped": f"金标集对应绑定 {g.get('rig')}，当前绑定 {rig.name}，跳过"}
    keys = ckpt["class_keys"]
    rows, expect, ids, notes = [], [], [], []
    for e in g["entries"]:
        unknown_keys = [k for k in e["sliders"] if k not in rig.keys]
        if unknown_keys:
            raise ValueError(f"金标 {e['id']} 用了绑定里没有的滑杆：{unknown_keys}")
        v = rig.defaults.copy()
        for k, val in e["sliders"].items():
            v[rig.keys.index(k)] = val
        rows.append(v)
        expect.append(e["expect"])
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
        pred = "unknown" if dec[i] < 0 else keys[dec[i]]
        ok = pred == expect[i]
        n_ok += ok
        per_cls.setdefault(expect[i], [0, 0])
        per_cls[expect[i]][0] += ok
        per_cls[expect[i]][1] += 1
        top = int(p[i].argmax())
        entries.append({"id": ids[i], "expect": expect[i], "pred": pred, "ok": bool(ok),
                        "top1": keys[top], "top1_prob": float(p[i, top]), "energy": float(en[i]), "note": notes[i]})
    known = [e for e in entries if e["expect"] != "unknown"]
    unk = [e for e in entries if e["expect"] == "unknown"]
    exp = np.array(expect)
    is_known = exp != "unknown"
    by_thr = {}
    for name, t in (thresholds or {}).items():
        d = decide(p, en, t, ckpt["min_confidence"])
        pred_t = np.array(["unknown" if k < 0 else keys[k] for k in d])
        by_thr[name] = {
            "threshold": float(t),
            "known_pass": int((d[is_known] >= 0).sum()),
            "known_correct": int((pred_t[is_known] == exp[is_known]).sum()),
            "n_known": int(is_known.sum()),
            "weird_reject": int((d[~is_known] < 0).sum()),
            "n_weird": int((~is_known).sum()),
            "known_pass_energy_only": int((en[is_known] <= t).sum()),
        }
    return {
        "file": rel_to_project(golden_path),
        "rig": rig.name,
        "n": len(entries),
        "n_known": len(known),
        "n_weird": len(unk),
        "known_correct": int(sum(e["ok"] for e in known)),
        "weird_correct": int(sum(e["ok"] for e in unk)),
        "by_threshold": by_thr,
        "accuracy": n_ok / len(entries) if entries else float("nan"),
        "known_accuracy": float(np.mean([e["ok"] for e in known])) if known else None,
        "unknown_detection": float(np.mean([e["ok"] for e in unk])) if unk else None,
        "per_expect": {k: {"ok": v[0], "n": v[1]} for k, v in per_cls.items()},
        "entries": entries,
    }


# ---------------------------------------------------------------- 整体评估


def evaluate_run(run_dir: Path, golden: Path | None = None, npz: list[Path] | None = None, write: bool = True,
                 extra: dict | None = None, log=print) -> dict:
    from .models import count_params, load_checkpoint

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
        "calibration": calib,
        "ood": ood,
        "thresholds": thresholds,
        "golden": None,
        "external": {},
    }

    rig = rig_of_ckpt(ckpt, canonical)
    if golden is not None and Path(golden).exists():
        metrics["golden"] = evaluate_golden(Path(golden), rig, classifier, canonical, ckpt, thr_all)
        for name, st in (metrics["golden"].get("by_threshold") or {}).items():
            by_rule[name].update({"golden_known_pass": st["known_pass"], "golden_known_correct": st["known_correct"],
                                  "golden_n_known": st["n_known"], "golden_weird_reject": st["weird_reject"],
                                  "golden_n_weird": st["n_weird"]})

    labels = load_labels()
    for p in npz or []:
        fs = load_features(p, canonical)
        norm = [labels.normalize(l) if l else None for l in fs.label]
        keep = np.array([k in keys for k in norm])  # 只评训练时启用的类别
        y = np.array([keys.index(k) for k, ok in zip(norm, keep) if ok], dtype=np.int64)
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

    if extra:
        metrics.update(extra)
    if write:
        save_json(metrics, run_dir / "metrics.json")
        (run_dir / "report.md").write_text(render_report(metrics, ckpt), encoding="utf-8", newline="\n")
    return metrics


# ---------------------------------------------------------------- 报告


def _f(v, nd=4):
    return "—" if v is None or (isinstance(v, float) and np.isnan(v)) else f"{v:.{nd}f}"


def render_report(m: dict, ckpt: dict) -> str:
    keys = ckpt["class_keys"]
    zh = dict(zip(ckpt["class_keys"], ckpt["class_zh"]))
    L = []
    L.append(f"# 训练报告：{m['run']}\n")
    L.append(f"> {DISCLAIMER_SYNTH if m['commercial_use_allowed'] else DISCLAIMER_REAL}\n")
    L.append("## 概况\n")
    L.append(f"- 模型：`{m['model']}`，参数量 {m['params']:,}")
    L.append(f"- 类别：{', '.join(f'{k}（{zh[k]}）' for k in keys)}")
    rig = ckpt.get("train_rig") or {}
    L.append(f"- 训练绑定：{rig.get('name', 'identity')}，哈希 `{str(ckpt.get('train_rig_hash'))[:16]}…`")
    L.append(f"- 可商用：{'是（只用合成数据）' if m['commercial_use_allowed'] else '否（含公开集）'}")
    L.append("- 数据来源：")
    for s in m["data_sources"]:
        L.append(f"  - {s['name']}（{s['kind']}）：训练 {s['n_train']}，验证 {s['n_val']}，划分 {s.get('split')}；授权：{s['license']}")
    if "training" in m:
        t = m["training"]
        L.append(f"- 训练：跑了 {t['epochs_run']} 个 epoch，最佳 epoch {t['best_epoch']}（验证 macro-F1 {_f(t['best_val_macro_f1'])}），用时 {t['seconds']:.0f} 秒")
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
    g = m.get("golden")
    L.append("\n## 金标捏脸集\n")
    if not g:
        L.append("- 未评估（没有提供金标文件）")
    elif "skipped" in g:
        L.append(f"- {g['skipped']}")
    else:
        L.append(f"- 文件 `{g['file']}`（绑定 {g['rig']}），共 {g['n']} 条：总准确率 **{_f(g['accuracy'])}**，"
                 f"已知类 {g['known_correct']}/{g['n_known']}，怪脸 {g['weird_correct']}/{g['n_weird']}（按当前阈值）")
        L.append("- 注意：这是按 FACS 原型手写的占位金标，与合成器同源，不能证明泛化（DESIGN §7）\n")
        L.append("| id | 期望 | 判定 | 对 | top1 | top1 概率 | energy | 备注 |\n| --- | --- | --- | --- | --- | --- | --- | --- |")
        for e in g["entries"]:
            L.append(f"| {e['id']} | {e['expect']} | {e['pred']} | {'✓' if e['ok'] else '✗'} | {e['top1']} | {_f(e['top1_prob'], 3)} | {_f(e['energy'], 2)} | {e['note']} |")
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
    ap = make_parser("python -m exprnet.evaluate", "重新评估一个训练产物，重写其中的 metrics.json 与 report.md。")
    ap.add_argument("--run", required=True, help="训练产物目录，如 artifacts/runs/rf_synth")
    ap.add_argument("--golden", default=None, help="金标集 json（默认用训练配置里的 golden）")
    ap.add_argument("--npz", nargs="*", default=[], help="额外评估的特征缓存 npz（有官方 test 划分时只评 test）")
    args = ap.parse_args(argv)
    run_dir = resolve_cli_path(args.run)
    old = load_json(run_dir / "metrics.json") if (run_dir / "metrics.json").exists() else {}
    golden = resolve_cli_path(args.golden) if args.golden else None
    if golden is None:
        import yaml

        snap = run_dir / "config.yaml"
        if snap.exists():
            from .common import resolve_path

            g = (yaml.safe_load(snap.read_text(encoding="utf-8")) or {}).get("golden")
            golden = resolve_path(g) if g else None
    extra = {"training": old["training"]} if "training" in old else None
    m = evaluate_run(run_dir, golden=golden, npz=[resolve_cli_path(p) for p in args.npz], extra=extra)
    print(f"[评估] 验证集 acc {m['val']['accuracy']:.4f}，macro-F1 {m['val']['macro_f1']:.4f}；"
          f"ECE {m['calibration']['ece_before']:.4f} → {m['calibration']['ece_after']:.4f}；"
          f"OOD AUROC {m['ood']['auroc']:.4f}，FPR95 {m['ood']['fpr95']:.4f}")
    g = m.get("golden")
    if g and "accuracy" in g:
        print(f"[评估] 金标准确率 {g['accuracy']:.4f}（{g['n']} 条；已知类 {g['known_correct']}/{g['n_known']}，怪脸 {g['weird_correct']}/{g['n_weird']}）")
    print(f"[评估] 报告：{rel_to_project(run_dir / 'report.md')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
