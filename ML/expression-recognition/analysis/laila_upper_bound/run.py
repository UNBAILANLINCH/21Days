"""laila 12 维捏脸输入下七类表情的可分性上限分析：一条命令重跑全部（口径与结论见同目录 REPORT.md）。

只调 exprnet 的库 API（coverage_report、train、load_checkpoint、FacsSynthesizer、project_cached 等），
不改 exprnet/ 与 configs/；缩减类别方案用的 labels / facs 在本脚本里临时构造，写到已忽略的 artifacts/ 下。

用法（在仓库根目录，Git Bash）：
    PYTHONPATH=ML/expression-recognition ML/expression-recognition/.venv/Scripts/python.exe \
        ML/expression-recognition/analysis/laila_upper_bound/run.py
    （脚本会自己把子项目根加进 sys.path，不设 PYTHONPATH 也能跑）
常用选项：
    --stage rigs|reduced|all   只跑四种绑定对比 / 只跑缩减类别方案 / 全部（默认）
    --reuse                    训练产物已存在（有 ckpt.pt）就不重训，直接重评
    --epochs N                 覆盖训练 epoch（只用于试跑；正式结果用默认配置）

产物（都在已忽略的 ML/expression-recognition/artifacts/laila_upper_bound/ 下）：
    coverage/<绑定>/coverage.{json,md}   覆盖度报告（与 python -m exprnet.coverage 同一套函数、同样默认参数）
    runs/<名字>/                         训练产物（与 python -m exprnet.train 同一套函数）
    configs/                             缩减类别方案的临时 labels / facs yaml
    summary.{json,md}                    全部汇总表，REPORT.md 里的数字都出自这里
"""

from __future__ import annotations

import argparse
import copy
import sys
import time
from pathlib import Path

import numpy as np
import yaml

PROJECT_ROOT = Path(__file__).resolve().parents[2]  # ML/expression-recognition/
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from exprnet.calibrate import decide, energy_np, softmax_np  # noqa: E402
from exprnet.canonical import load_canonical  # noqa: E402
from exprnet.common import ARTIFACTS_DIR, CONFIG_DIR, load_labels, load_yaml, portable_path, save_json  # noqa: E402
from exprnet.coverage import coverage_report, render_markdown  # noqa: E402
from exprnet.datasets import project_cached  # noqa: E402
from exprnet.evaluate import classification_metrics, logits_of  # noqa: E402
from exprnet.facs import FacsSynthesizer  # noqa: E402
from exprnet.models import load_checkpoint  # noqa: E402
from exprnet.rig import load_rig  # noqa: E402
from exprnet.train import load_config, train  # noqa: E402

HERE = Path(__file__).resolve().parent
OUT = ARTIFACTS_DIR / "laila_upper_bound"
MODEL = "resmlp"

# 四种绑定：两份候选（分析用，不是绑定）+ 两个对照组
RIGS = [
    {"key": "conservative", "zh": "laila 保守候选", "path": portable_path(HERE / "laila_candidate_conservative.yaml")},
    {"key": "optimistic", "zh": "laila 乐观候选", "path": portable_path(HERE / "laila_candidate_optimistic.yaml")},
    {"key": "sample_rig", "zh": "sample_rig（对照）", "path": "configs/rigs/sample_rig.yaml"},
    {"key": "identity", "zh": "单位绑定（天花板）", "path": None},
]

# 缩减类别方案（只在乐观候选上重训）。按四种绑定对比里实际混在一起的类对来定（见 REPORT.md §5）：
#   惊讶↔恐惧（恐惧的 AU4 与 AU2 抢同一根眉滑杆）、中性↔厌恶（厌恶的诊断动作 AU9 够不到）。
# merge 里的类别合并成一个新类，drop 里的类别不再训练、不再评估；
# 合并类的合成样本：各成员类的变体权重先各自归一，再平均分配，成员类各占一半。
# drop_invisible_variants：不改类别，只把「核心 AU 在乐观候选下投影后几乎全丢」的变体从训练数据里去掉
#   （变体级相对残差 ≥ 阈值），用来区分「类别分不开」和「目标里混进了这张脸根本做不出来的变体」。
REDUCED_SETS = [
    {"key": "r6_surprise_fear", "zh": "六类：惊讶+恐惧合并", "drop": [], "merge": {"surprise_fear": ["surprise", "fear"]}},
    {"key": "r6_no_disgust", "zh": "六类：去掉厌恶", "drop": ["disgust"], "merge": {}},
    {"key": "r5_no_disgust_surprise_fear", "zh": "五类：去掉厌恶，惊讶+恐惧合并", "drop": ["disgust"],
     "merge": {"surprise_fear": ["surprise", "fear"]}},
    {"key": "r7_visible_variants", "zh": "七类对照：只去掉做不出来的变体", "drop": [], "merge": {},
     "drop_invisible_variants": 0.9},
]
MERGED_ZH = {"sad_fear": "悲伤/恐惧", "surprise_fear": "惊讶/恐惧", "sad_disgust": "悲伤/厌恶", "angry_disgust": "愤怒/厌恶"}

GATE_RECALL = 0.65  # 规格 §11.2 每类召回门槛
GATE_REJECT = 0.10  # 规格 §11.2 已知类被拒识比例上限
PROTO_INTENSITY = 0.6  # 变体级 AU 保留分析用的统一强度（C 档中上）
KNN_K = 15


# ================================================================ 工具


def log(msg: str) -> None:
    print(msg, flush=True)


def f4(v) -> str:
    return "—" if v is None else f"{v:.4f}"


def f3(v) -> str:
    return "—" if v is None else f"{v:.3f}"


def md_table(header: list[str], rows: list[list]) -> list[str]:
    out = ["| " + " | ".join(header) + " |", "| " + " | ".join("---" for _ in header) + " |"]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return out


# ================================================================ 评估：部署视角 + 最终判定口径


def final_decision_metrics(y: np.ndarray, d: np.ndarray, keys: list[str]) -> dict:
    """最终判定口径（规格 §7.2、§11.2）：d = -1 表示「认不出」，被拒识也算该类漏判。
    混淆矩阵多一列 unknown；召回分母是该类全部样本；precision 分母是被判成该类的样本。"""
    C = len(keys)
    cm = np.zeros((C, C + 1), dtype=np.int64)
    for t, p in zip(y, d):
        cm[t, C if p < 0 else p] += 1
    per, f1s = [], []
    for c in range(C):
        tp = cm[c, c]
        sup = int(cm[c].sum())
        pred_n = int(cm[:, c].sum())
        prec = tp / pred_n if pred_n else 0.0
        rec = tp / sup if sup else 0.0
        f1 = 2 * prec * rec / (prec + rec) if prec + rec else 0.0
        per.append({"key": keys[c], "precision": float(prec), "recall": float(rec), "f1": float(f1),
                    "reject_rate": float(cm[c, C] / sup) if sup else 0.0, "support": sup})
        f1s.append(f1)
    return {
        "accuracy": float((d == y).mean()),          # 判对且未被拒（已知类样本里）
        "known_pass_rate": float((d >= 0).mean()),   # 已知类未被拒识的比例
        "macro_f1": float(np.mean(f1s)),
        "per_class": per,
        "confusion_with_unknown": cm.tolist(),
    }


def deploy_eval(run_dir: Path) -> dict:
    """在训练产物的验证集（绑定投影后 = 部署视角）上算 argmax 指标与最终判定指标，另报留出怪脸拒识率。"""
    clf, ckpt, canonical = load_checkpoint(run_dir)
    keys = ckpt["class_keys"]
    T, thr, mc = float(ckpt["temperature"]), float(ckpt["energy_threshold"]), float(ckpt["min_confidence"])
    mask = canonical.mask_vector
    ev = np.load(run_dir / "eval_data.npz")
    y = ev["y_val"]
    lz = logits_of(clf, ev["xp_val"], mask)
    p, e = softmax_np(lz / T), energy_np(lz, T)
    am = classification_metrics(y, p.argmax(-1), keys)
    fin = final_decision_metrics(y, decide(p, e, thr, mc), keys)
    lzn = logits_of(clf, ev["negp_heldout"], mask)
    neg_rej = float((decide(softmax_np(lzn / T), energy_np(lzn, T), thr, mc) < 0).mean())
    raw = classification_metrics(y, logits_of(clf, ev["x_val"], mask).argmax(-1), keys)
    worst = min(am["per_class"], key=lambda r: r["recall"])
    worst_f = min(fin["per_class"], key=lambda r: r["recall"])
    reject = 1.0 - fin["known_pass_rate"]
    gate = {  # 规格 §11.2 各门槛在合成验证集上的对照（只是上限参照，不是验收）
        "final_accuracy_ge_0.80": fin["accuracy"] >= 0.80,
        "final_macro_f1_ge_0.80": fin["macro_f1"] >= 0.80,
        f"every_final_recall_ge_{GATE_RECALL}": all(p["recall"] >= GATE_RECALL for p in fin["per_class"]),
        f"known_reject_le_{GATE_REJECT}": reject <= GATE_REJECT,
        "heldout_neg_reject_ge_0.80": neg_rej >= 0.80,
        "failed_recall_classes": [p["key"] for p in fin["per_class"] if p["recall"] < GATE_RECALL],
    }
    return {
        "run": run_dir.name, "class_keys": keys, "class_zh": ckpt["class_zh"], "rig_hash": ckpt["train_rig_hash"],
        "temperature": T, "energy_threshold": thr, "min_confidence": mc, "n_val": int(len(y)),
        "argmax": am, "final": fin, "known_reject_rate": reject, "heldout_neg_reject_rate": neg_rej,
        "raw_view": {"accuracy": raw["accuracy"], "macro_f1": raw["macro_f1"]},
        "worst_recall": {"key": worst["key"], "recall": worst["recall"]},
        "worst_recall_final": {"key": worst_f["key"], "recall": worst_f["recall"]},
        "gate": gate,
    }


# ================================================================ 语义保真：用完整空间训出的读法去读投影后的脸


def cross_read(id_run: Path, split: dict, keys: list[str], key_map: dict[str, str | None]) -> dict:
    """可分 ≠ 读得出：投影后的点彼此分得开，不代表那张脸还带着该类的诊断动作。
    这里用「只见过完整 51 维表情、没见过绑定投影」的读法去读投影后的验证集：
      - resmlp：单位绑定训练产物（identity_resmlp），按它自己的温度 / 阈值做最终判定；
      - kNN   ：在完整空间（未投影）的训练集上拟合，预测投影后的验证集（无模型）。
    key_map：单位绑定模型的类别 key → 当前类别集的 key（合并类映射到合并后的 key，被去掉的类为 None）。
    被读成「已去掉的类」单独计一列 other。"""
    act = split["active"]
    C = len(keys)
    y = split["y_va"]
    clf, ck, canonical = load_checkpoint(id_run)
    T, thr, mc = float(ck["temperature"]), float(ck["energy_threshold"]), float(ck["min_confidence"])
    idx_map = np.array([keys.index(key_map[k]) if key_map.get(k) in keys else C for k in ck["class_keys"]])
    lz = logits_of(clf, split["p_va"], canonical.mask_vector)
    p, e = softmax_np(lz / T), energy_np(lz, T)
    pred = idx_map[p.argmax(-1)]
    d = decide(p, e, thr, mc)
    pred_final = np.where(d < 0, -1, idx_map[np.maximum(d, 0)])
    out = {}
    for name, pr in (("resmlp", pred), ("resmlp_final", pred_final)):
        cm = np.zeros((C, C + 2), dtype=np.int64)  # 列：各类 + other（读成已去掉的类）+ unknown
        for t, q in zip(y, pr):
            cm[t, C + 1 if q < 0 else q] += 1
        rec = [float(cm[c, c] / max(1, cm[c].sum())) for c in range(C)]
        out[name] = {"accuracy": float(np.trace(cm[:, :C]) / len(y)), "recall": dict(zip(keys, rec)),
                     "confusion": cm.tolist()}
    Xtr = split["x_tr"][:, act].astype(np.float64)
    kp = clf_knn(Xtr, split["y_tr"], split["p_va"][:, act].astype(np.float64), C)
    km = classification_metrics(y, kp, keys)
    out["knn_full_space"] = {"accuracy": km["accuracy"], "recall": {p_["key"]: p_["recall"] for p_ in km["per_class"]},
                             "confusion": km["confusion"]}
    return out



# ================================================================ 与模型无关的分类器（numpy 实现，没装 sklearn）


def _sqdist(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.maximum((a**2).sum(1)[:, None] + (b**2).sum(1)[None, :] - 2 * a @ b.T, 0.0)


def clf_ncm(Xtr, ytr, Xte, C):
    mus = np.stack([Xtr[ytr == c].mean(0) for c in range(C)])
    return _sqdist(Xte, mus).argmin(1)


def clf_knn(Xtr, ytr, Xte, C, k=KNN_K, chunk=512):
    out = np.empty(len(Xte), dtype=np.int64)
    for s in range(0, len(Xte), chunk):
        d = _sqdist(Xte[s:s + chunk], Xtr)
        nn = np.argpartition(d, k, axis=1)[:, :k]
        votes = np.zeros((len(nn), C))
        np.add.at(votes, (np.repeat(np.arange(len(nn)), k), ytr[nn].ravel()), 1)
        out[s:s + chunk] = votes.argmax(1)
    return out


def clf_lda(Xtr, ytr, Xte, C, shrink=1e-2):
    """线性判别分析：共享协方差 + 收缩正则（投影后的数据落在 12 维子空间里，协方差奇异），等先验。"""
    mus = np.stack([Xtr[ytr == c].mean(0) for c in range(C)])
    R = Xtr - mus[ytr]
    S = R.T @ R / max(1, len(Xtr) - C)
    D = S.shape[0]
    S = (1 - shrink) * S + shrink * (np.trace(S) / D) * np.eye(D) + 1e-6 * np.eye(D)
    W = np.linalg.solve(S, mus.T)  # [D, C]
    g = Xte @ W - 0.5 * (mus * W.T).sum(1)[None, :]
    return g.argmax(1)


CLASSIFIERS = {"ncm": ("最近类均值", clf_ncm), "knn": (f"{KNN_K}-近邻", clf_knn), "lda": ("LDA（收缩）", clf_lda)}


def rebuild_split(cfg: dict, canonical, synth, rig):
    """按 datasets.build_train_data 的同一套规则重建合成数据的训练 / 验证划分与投影（投影命中同一份缓存）。"""
    src = next(s for s in cfg["data"]["sources"] if s["kind"] == "synthetic")
    n, s_seed = int(src["n_per_class"]), int(src.get("seed", cfg.get("seed", 0)))
    X, y = synth.generate(n, s_seed)
    va = np.random.default_rng(s_seed + 1).random(len(X)) < float(cfg["data"].get("val_fraction", 0.15))
    mask = canonical.mask_vector
    xtr, xva = X[~va].astype(np.float32), X[va].astype(np.float32)
    ptr = project_cached(rig, xtr, mask)
    pva = project_cached(rig, xva, mask)
    return {"x_tr": xtr * mask, "y_tr": y[~va], "p_tr": ptr, "x_va": xva * mask, "y_va": y[va], "p_va": pva,
            "active": canonical.active_indices}


def model_free(split: dict, canonical, keys: list[str], eval_npz: Path | None = None) -> dict:
    act = canonical.active_indices
    C = len(keys)
    if eval_npz is not None:  # 自检：重建的验证集投影应与训练产物里保存的一致
        ev = np.load(eval_npz)
        if ev["xp_val"].shape != split["p_va"].shape or not np.allclose(ev["xp_val"], split["p_va"], atol=1e-6):
            raise RuntimeError("重建的验证集与训练产物不一致，模型无关分类器的对比失去意义")
    out = {}
    for view, (a, b) in {"projected": ("p_tr", "p_va"), "raw": ("x_tr", "x_va")}.items():
        Xtr, Xte = split[a][:, act].astype(np.float64), split[b][:, act].astype(np.float64)
        for name, (_, fn) in CLASSIFIERS.items():
            if view == "raw" and name != "knn":
                continue  # 原值视角只跑 kNN 作天花板参照
            m = classification_metrics(split["y_va"], fn(Xtr, split["y_tr"], Xte, C), keys)
            out[f"{view}_{name}"] = m
    return out


def class_separation(split: dict, canonical, keys: list[str]) -> dict:
    """投影后的类间可分指数：两类均值距离 / 类内均方根离散（无模型，越小越难分）。"""
    act = canonical.active_indices
    X, y = split["p_va"][:, act].astype(np.float64), split["y_va"]
    mus = [X[y == c].mean(0) for c in range(len(keys))]
    spr = [np.sqrt(((X[y == c] - mus[c]) ** 2).sum(1).mean()) for c in range(len(keys))]
    out = {}
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            out[f"{keys[i]}|{keys[j]}"] = float(np.linalg.norm(mus[i] - mus[j]) / np.sqrt((spr[i] ** 2 + spr[j] ** 2) / 2))
    return out


def confused_pairs(cm: list[list[int]], keys: list[str], top: int = 6, min_rate: float = 0.02) -> list[dict]:
    """对称混淆率 (C[i,j]+C[j,i]) / (n_i+n_j)，只看 C×C 部分。"""
    cm = np.asarray(cm)[:, : len(keys)]
    sup = np.asarray(cm).sum(1)
    pairs = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            r = (cm[i, j] + cm[j, i]) / max(1, sup[i] + sup[j])
            if r >= min_rate:
                pairs.append({"pair": f"{keys[i]}|{keys[j]}", "rate": float(r),
                              f"{keys[i]}→{keys[j]}": int(cm[i, j]), f"{keys[j]}→{keys[i]}": int(cm[j, i])})
    return sorted(pairs, key=lambda p: -p["rate"])[:top]


# ================================================================ AU 层面：哪个 AU 在该绑定下够不到


def au_retention(rig, canonical, synth) -> dict:
    """单个 AU（强度 1）投影后保留的能量比例 1 − ||a − â||² / ||a||²。"""
    mask = canonical.mask_vector
    out = {}
    for au, v in synth.au_vec.items():
        a = (v * mask).astype(np.float64)
        if a.sum() <= 0:
            continue  # AU64（眼球朝下）全在屏蔽维
        _, ah = rig.project(a[None], mask)
        out[au] = float(1 - ((a - ah[0]) ** 2).sum() / (a**2).sum())
    return out


def variant_retention(rig, canonical, synth, keys: list[str]) -> list[dict]:
    """每个情绪变体按统一强度摆出核心 AU（choose 取第一项，optional 不加）→ 投影 →
    逐 AU 保留率 r_k = Σ a_k·x̂ / Σ a_k·x（一根滑杆正负两侧冲突、或基够不到，都会让 r_k 掉下来）。"""
    mask = canonical.mask_vector
    rows = []
    for key in keys:
        for vi, var in enumerate(synth.variants[key]):
            aus = dict(var.aus)
            if var.choose:
                aus.update(var.choose[0])
            if not aus:
                continue
            x = np.zeros(canonical.dim)
            for au in aus:
                x = np.maximum(x, synth.au_vec[au] * PROTO_INTENSITY)
            x *= mask
            _, xh = rig.project(x[None], mask)
            xh = xh[0].astype(np.float64)
            per_au = {}
            for au in aus:
                a = synth.au_vec[au] * mask
                den = float(a @ x)
                per_au[au] = float(a @ np.minimum(xh, x) / den) if den > 0 else None
            rows.append({"class": key, "variant": vi, "source": var.source, "weight": var.weight,
                         "aus": list(aus), "rel_residual": float(((x - xh) ** 2).sum() / max((x**2).sum(), 1e-9)),
                         "au_kept": per_au})
    return rows


# ================================================================ 缩减类别：临时 labels / facs


def build_reduced_configs(spec: dict, base_labels, base_facs: dict, cfg_dir: Path,
                          invisible: dict[str, list[int]] | None = None) -> tuple[str, str, list[str]]:
    """invisible：{类别: [要去掉的变体下标]}（只在 drop_invisible_variants 方案里用）。"""
    merged_members = {m for ms in spec["merge"].values() for m in ms}
    base_facs = copy.deepcopy(base_facs)
    for k, drop_idx in (invisible or {}).items():
        vs = base_facs["emotions"][k]["variants"]
        base_facs["emotions"][k]["variants"] = [v for i, v in enumerate(vs) if i not in set(drop_idx)]
    classes, emo = [], copy.deepcopy(base_facs["emotions"])
    zh_of = dict(zip(base_labels.keys, base_labels.zh))
    placed = set()
    for k in base_labels.keys:
        if k in spec["drop"]:
            continue
        if k in merged_members:
            new = next(n for n, ms in spec["merge"].items() if k in ms)
            if new in placed:
                continue
            placed.add(new)
            members = spec["merge"][new]
            variants = []
            for m in members:
                vs = base_facs["emotions"][m]["variants"]
                tot = sum(float(v.get("weight", 1.0)) for v in vs)
                for v in vs:
                    v2 = copy.deepcopy(v)
                    v2["weight"] = float(v.get("weight", 1.0)) / tot / len(members)
                    v2["note"] = f"[合并自 {m}] " + str(v.get("note", ""))
                    variants.append(v2)
            emo[new] = {"variants": variants}
            classes.append({"key": new, "zh": MERGED_ZH.get(new, "/".join(zh_of[m] for m in members))})
        else:
            classes.append({"key": k, "zh": zh_of[k]})
    facs = copy.deepcopy(base_facs)
    facs["emotions"] = emo
    cfg_dir.mkdir(parents=True, exist_ok=True)
    lp, fp = cfg_dir / f"labels_{spec['key']}.yaml", cfg_dir / f"facs_{spec['key']}.yaml"
    head = f"# 分析脚本临时生成（{spec['zh']}），只供 analysis/laila_upper_bound 使用，不是正式类别配置\n"
    for path, obj in ((lp, {"classes": classes, "aliases": {}, "datasets": {}}), (fp, facs)):
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(head)
            yaml.safe_dump(obj, f, allow_unicode=True, sort_keys=False)
    return portable_path(lp), portable_path(fp), [c["key"] for c in classes]


# ================================================================ 训练


def train_cfg(base: dict, rig_path, epochs: int | None, labels_path=None, facs_path=None) -> dict:
    cfg = copy.deepcopy(base)
    cfg["model"]["name"] = MODEL
    cfg["rig"] = rig_path
    cfg["golden"] = None  # 规格 §7.1：没有真实标注集时明确不评金标
    if labels_path:
        cfg["labels"] = labels_path
    if facs_path:
        cfg["facs"] = facs_path
    if epochs is not None:
        cfg["optim"]["epochs"] = epochs
    return cfg


def run_training(cfg: dict, name: str, reuse: bool) -> Path:
    run_dir = OUT / "runs" / name
    if reuse and (run_dir / "ckpt.pt").exists():
        log(f"[复用] {name} 已有训练产物，跳过训练")
        return run_dir
    return train(cfg, name, out_root=OUT / "runs", log=log)


# ================================================================ 主流程


def stage_rigs(base_cfg, canonical, labels, facs_cfg, args) -> dict:
    synth = FacsSynthesizer(canonical, labels.keys, cfg=facs_cfg)
    Xc, yc = synth.generate(500, 0)  # 与 python -m exprnet.coverage 的默认参数一致
    results, splits = {}, {}
    for r in RIGS:
        t0 = time.time()
        rig = load_rig(r["path"], canonical)
        log(f"\n==================== 绑定 {r['key']}（{rig.name}，{rig.num_sliders} 维） ====================")
        cov = coverage_report(rig, canonical, Xc, yc, labels.keys, "FACS 合成原型（每类 500，种子 0）")
        cdir = OUT / "coverage" / r["key"]
        cdir.mkdir(parents=True, exist_ok=True)
        save_json(cov, cdir / "coverage.json")
        (cdir / "coverage.md").write_text(render_markdown(cov), encoding="utf-8", newline="\n")
        cfg = train_cfg(base_cfg, r["path"], args.epochs)
        run_dir = run_training(cfg, f"{r['key']}_{MODEL}", args.reuse)
        dep = deploy_eval(run_dir)
        split = rebuild_split(cfg, canonical, synth, rig)
        mf = model_free(split, canonical, labels.keys, run_dir / "eval_data.npz")
        results[r["key"]] = {
            "zh": r["zh"], "rig_name": rig.name, "rig_path": r["path"], "num_sliders": rig.num_sliders,
            "rig_hash": rig.config_hash(),
            "coverage": {"summary": cov["summary"], "per_class": cov["per_class"], "sliders": cov["sliders"]},
            "deploy": dep, "model_free": mf,
            "separation": class_separation(split, canonical, labels.keys),
            "pairs_resmlp": confused_pairs(dep["argmax"]["confusion"], labels.keys),
            "pairs_knn": confused_pairs(mf["projected_knn"]["confusion"], labels.keys),
            "au_retention": au_retention(rig, canonical, synth),
            "variant_retention": variant_retention(rig, canonical, synth, labels.keys),
            "seconds": time.time() - t0,
        }
        splits[r["key"]] = split
        log(f"[汇总] {r['key']}：残差 {cov['summary']['mean_rel_residual']:.4f}，resmlp acc {dep['argmax']['accuracy']:.4f} "
            f"macro-F1 {dep['argmax']['macro_f1']:.4f}，最低召回 {dep['worst_recall']['key']} {dep['worst_recall']['recall']:.3f}；"
            f"kNN {mf['projected_knn']['accuracy']:.4f}，LDA {mf['projected_lda']['accuracy']:.4f}，NCM {mf['projected_ncm']['accuracy']:.4f}")
    id_run = OUT / "runs" / f"identity_{MODEL}"
    ident = {k: k for k in labels.keys}
    for k, split in splits.items():
        results[k]["cross_read"] = cross_read(id_run, split, labels.keys, ident)
        cr = results[k]["cross_read"]
        log(f"[语义保真] {k}：完整空间 resmlp 读投影脸 acc {cr['resmlp']['accuracy']:.4f}，kNN {cr['knn_full_space']['accuracy']:.4f}")
    return results


def stage_reduced(base_cfg, canonical, labels, facs_cfg, args) -> dict:
    opt = next(r for r in RIGS if r["key"] == "optimistic")
    rig = load_rig(opt["path"], canonical)
    id_run = OUT / "runs" / f"identity_{MODEL}"
    if not (id_run / "ckpt.pt").exists():
        raise SystemExit("缺少单位绑定训练产物，先跑 --stage rigs（或 all）")
    full_synth = FacsSynthesizer(canonical, labels.keys, cfg=facs_cfg)
    results = {}
    for spec in REDUCED_SETS:
        t0 = time.time()
        log(f"\n==================== 缩减方案 {spec['key']}（{spec['zh']}，乐观候选） ====================")
        invisible: dict[str, list[int]] = {}
        if spec.get("drop_invisible_variants"):
            for v in variant_retention(rig, canonical, full_synth, labels.keys):
                if v["rel_residual"] >= spec["drop_invisible_variants"]:
                    invisible.setdefault(v["class"], []).append(v["variant"])
            log(f"[缩减] 去掉投影后几乎不可见的变体：{invisible}")
        lp, fp, keys = build_reduced_configs(spec, labels, facs_cfg, OUT / "configs", invisible)
        cfg = train_cfg(base_cfg, opt["path"], args.epochs, labels_path=lp, facs_path=fp)
        run_dir = run_training(cfg, f"optimistic_{spec['key']}_{MODEL}", args.reuse)
        dep = deploy_eval(run_dir)
        synth = FacsSynthesizer(canonical, keys, cfg=load_yaml(PROJECT_ROOT / fp))
        split = rebuild_split(cfg, canonical, synth, rig)
        mf = model_free(split, canonical, keys, run_dir / "eval_data.npz")
        key_map = {k: k for k in keys}
        for new, ms in spec["merge"].items():
            key_map.update({m: new for m in ms})
        for k in spec["drop"]:
            key_map[k] = None
        results[spec["key"]] = {"zh": spec["zh"], "drop": spec["drop"], "merge": spec["merge"], "class_keys": keys,
                                "dropped_variants": invisible,
                                "deploy": dep, "model_free": mf,
                                "cross_read": cross_read(id_run, split, keys, key_map),
                                "pairs_resmlp": confused_pairs(dep["argmax"]["confusion"], keys),
                                "seconds": time.time() - t0}
        log(f"[汇总] {spec['key']}：acc {dep['argmax']['accuracy']:.4f} macro-F1 {dep['argmax']['macro_f1']:.4f}，"
            f"最低召回 {dep['worst_recall']['key']} {dep['worst_recall']['recall']:.3f}；kNN {mf['projected_knn']['accuracy']:.4f}")
    return results


# ================================================================ 汇总 markdown


def gate_table(items: list[tuple[str, dict]]) -> list[str]:
    """规格 §11.2 各门槛在合成验证集上的对照（上限参照，不是验收）。"""
    out = ["\n### 规格 §11.2 门槛对照（合成验证集，最终判定口径；只是上限参照）\n"]
    rows = []
    for name, d in items:
        g, fin = d["gate"], d["final"]
        ok = lambda b: "过" if b else "**不过**"  # noqa: E731
        rows.append([name, f"{f3(fin['accuracy'])} {ok(g['final_accuracy_ge_0.80'])}",
                     f"{f3(fin['macro_f1'])} {ok(g['final_macro_f1_ge_0.80'])}",
                     f"{d['worst_recall_final']['key']} {f3(d['worst_recall_final']['recall'])} {ok(g[f'every_final_recall_ge_{GATE_RECALL}'])}",
                     f"{f3(d['known_reject_rate'])} {ok(g[f'known_reject_le_{GATE_REJECT}'])}",
                     f"{f3(d['heldout_neg_reject_rate'])} {ok(g['heldout_neg_reject_ge_0.80'])}"])
    out += md_table(["方案", "最终判对率 ≥0.80", "最终 macro-F1 ≥0.80", f"每类最终召回 ≥{GATE_RECALL}（最低类）",
                     f"已知类被拒 ≤{GATE_REJECT}", "怪脸拒识 ≥0.80"], rows)
    return out


def render_summary(S: dict) -> str:
    L = ["# laila 可分性上限分析：汇总表（run.py 自动生成）\n",
         "> 合成 FACS 先验 + 候选对应，验证集与训练集同源：这些数字是**上限**，真人判读只会更差。两份 laila 候选都不是绑定。\n"]
    R = S.get("rigs") or {}
    if R:
        keys = next(iter(R.values()))["deploy"]["class_keys"]
        L.append("## 1. 四种绑定对比（resmlp，部署视角 = 验证集绑定投影后）\n")
        rows = []
        for k, r in R.items():
            d, mf, cv = r["deploy"], r["model_free"], r["coverage"]["summary"]
            rows.append([r["zh"], r["num_sliders"], f4(cv["mean_rel_residual"]), f4(d["argmax"]["accuracy"]),
                         f4(d["argmax"]["macro_f1"]), f"{d['worst_recall']['key']} {f3(d['worst_recall']['recall'])}",
                         f4(d["known_reject_rate"]), f4(d["final"]["accuracy"]), f4(d["final"]["macro_f1"]),
                         f"{d['worst_recall_final']['key']} {f3(d['worst_recall_final']['recall'])}",
                         f4(d["heldout_neg_reject_rate"]),
                         f4(mf["projected_ncm"]["accuracy"]), f4(mf["projected_knn"]["accuracy"]),
                         f4(mf["projected_lda"]["accuracy"]), f4(mf["raw_knn"]["accuracy"])])
        L += md_table(["绑定", "维数", "投影残差均值", "acc", "macro-F1", "最低召回", "已知类被拒比例",
                       "最终判对率", "最终 macro-F1", "最终最低召回", "留出怪脸拒识率", "NCM acc", f"{KNN_K}NN acc",
                       "LDA acc", f"原值 {KNN_K}NN acc"], rows)
        L.append("\n- 「最终」= 规格 §7.2 判定（energy > 阈值 或 max(probs) < min_confidence 判认不出），被拒识计为该类漏判。")
        L.append("- NCM / kNN / LDA 在投影后的规范向量上训练与测试（同一划分）；「原值 kNN」不投影，四种绑定相同，是合成数据本身的可分上限。\n")
        L += gate_table([(r["zh"], r["deploy"]) for r in R.values()])
        if all("cross_read" in r for r in R.values()):
            L.append("\n## 1b. 语义保真：只见过完整表情的读法去读投影后的脸\n")
            L.append("可分 ≠ 读得出。上表的模型是在投影后的数据上训的，它学会的是「这张 12 维脸在合成先验里对应哪类」；"
                     "这里改用只见过完整 51 维表情的读法（单位绑定 resmlp、完整空间 kNN）去读投影后的验证集，"
                     "近似「知道真实表情长什么样的观察者」看这张脸。\n")
            rows = []
            for r in R.values():
                cr = r["cross_read"]
                for tag, name in (("resmlp argmax", "resmlp"), ("resmlp 最终", "resmlp_final"),
                                  ("完整空间 kNN", "knn_full_space")):
                    cells = [f"**{f3(v)}**" if v < GATE_RECALL else f3(v) for v in (cr[name]["recall"][k] for k in keys)]
                    rows.append([r["zh"], tag, f4(cr[name]["accuracy"])] + cells)
            L += md_table(["绑定", "读法", "acc"] + keys, rows)
            for k in ("conservative", "optimistic"):
                if k in R:
                    cm = R[k]["cross_read"]["resmlp_final"]["confusion"]
                    L.append(f"\n{R[k]['zh']}：单位绑定 resmlp 最终判定读投影脸的混淆（行 = 真值；other = 读成已去掉的类）：\n")
                    L += md_table(["真值 \\ 读成"] + keys + ["other", "unknown"], [[kk] + row for kk, row in zip(keys, cm)])
            L.append("")

        L.append("## 2. 逐类召回（resmlp argmax / 最终判定 / kNN）\n")
        rows = []
        for k, r in R.items():
            d, mf = r["deploy"], r["model_free"]
            for tag, per in (("resmlp", d["argmax"]["per_class"]), ("resmlp 最终", d["final"]["per_class"]),
                             ("kNN", mf["projected_knn"]["per_class"]), ("LDA", mf["projected_lda"]["per_class"])):
                cells = []
                for p in per:
                    v = f3(p["recall"])
                    cells.append(f"**{v}**" if p["recall"] < GATE_RECALL else v)
                rows.append([r["zh"], tag] + cells)
        L += md_table(["绑定", "口径"] + keys, rows)
        L.append(f"\n加粗 = 低于规格 §11.2 的每类召回门槛 {GATE_RECALL}。\n")

        L.append("## 3. 覆盖度：逐类投影相对残差、不可达规范基\n")
        rows = [[r["zh"]] + [f4(r["coverage"]["per_class"][c]["mean_rel_residual"]) for c in keys] for r in R.values()]
        L += md_table(["绑定"] + keys, rows)
        L.append("")
        for r in R.values():
            s = r["coverage"]["summary"]
            L.append(f"- **{r['zh']}**：数据里用到但没有控制驱动的基：{', '.join(s['unreachable_but_used']) or '无'}；"
                     f"有控制但保留 < 70%：{', '.join(s['poorly_covered_dims']) or '无'}")
        L.append("")
        for k in ("conservative", "optimistic"):
            if k in R:
                L.append(f"\n{R[k]['zh']} 逐控制使用率与饱和比例：\n")
                L += md_table(["控制", "使用率", "顶到最大", "顶到最小", "平均 |值|"],
                              [[s["key"], f3(s["used_rate"]), f3(s["at_max_rate"]), f3(s["at_min_rate"]), f3(s["mean_abs"])]
                               for s in R[k]["coverage"]["sliders"]])
        L.append("")

        L.append("## 4. 混淆矩阵\n")
        for r in R.values():
            d, mf = r["deploy"], r["model_free"]
            L.append(f"### {r['zh']}\n")
            L.append("resmlp（argmax，行 = 真值，列 = 预测）：\n")
            L += md_table(["真值 \\ 预测"] + keys, [[k] + row for k, row in zip(keys, d["argmax"]["confusion"])])
            L.append("\nresmlp 最终判定（含认不出列）：\n")
            L += md_table(["真值 \\ 预测"] + keys + ["unknown"],
                          [[k] + row for k, row in zip(keys, d["final"]["confusion_with_unknown"])])
            L.append(f"\n{KNN_K}-近邻（投影后）：\n")
            L += md_table(["真值 \\ 预测"] + keys, [[k] + row for k, row in zip(keys, mf["projected_knn"]["confusion"])])
            L.append("")

        L.append("## 5. 混在一起的类对（对称混淆率 ≥ 2%，前 6）\n")
        rows = []
        for r in R.values():
            for tag, pairs in (("resmlp", r["pairs_resmlp"]), ("kNN", r["pairs_knn"])):
                for p in pairs:
                    a, b = p["pair"].split("|")
                    rows.append([r["zh"], tag, p["pair"], f3(p["rate"]), p[f"{a}→{b}"], p[f"{b}→{a}"],
                                 f3(r["separation"][p["pair"]])])
        L += md_table(["绑定", "分类器", "类对", "对称混淆率", "前→后", "后→前", "类间可分指数"], rows)
        L.append("\n类间可分指数 = 两类投影后均值距离 / 类内均方根离散，越小越难分（无模型）。\n")

        L.append("## 6. 单个 AU（强度 1）投影后保留的能量比例\n")
        aus = list(next(iter(R.values()))["au_retention"])
        L += md_table(["AU"] + [r["zh"] for r in R.values()],
                      [[au] + [f3(r["au_retention"][au]) for r in R.values()] for au in aus])
        L.append("")

        L.append(f"## 7. 情绪变体逐 AU 保留率（核心 AU 统一强度 {PROTO_INTENSITY}，choose 取第一项）\n")
        L.append("单元格：该变体投影后的相对残差；括号里是保留率 < 0.5 的 AU（= 在该绑定下丢掉的动作）。\n")
        base = next(iter(R.values()))["variant_retention"]
        rows = []
        for i, v in enumerate(base):
            cells = []
            for r in R.values():
                vr = r["variant_retention"][i]
                lost = [au for au, k in vr["au_kept"].items() if k is not None and k < 0.5]
                cells.append(f"{vr['rel_residual']:.2f}" + (f"（丢 {'+'.join(lost)}）" if lost else ""))
            rows.append([v["class"], v["variant"], v["source"], "+".join(v["aus"])] + cells)
        L += md_table(["类", "#", "出处", "核心 AU"] + [r["zh"] for r in R.values()], rows)
        L.append("")

    Q = S.get("reduced") or {}
    if Q:
        L.append("## 8. 缩减类别方案（乐观候选上重训 resmlp）\n")
        rows = []
        opt = R.get("optimistic")
        if opt:
            d = opt["deploy"]
            rows.append(["七类（基线）", ", ".join(d["class_keys"]), f4(d["argmax"]["accuracy"]), f4(d["argmax"]["macro_f1"]),
                         f"{d['worst_recall']['key']} {f3(d['worst_recall']['recall'])}", f4(d["final"]["macro_f1"]),
                         f"{d['worst_recall_final']['key']} {f3(d['worst_recall_final']['recall'])}",
                         f4(d["final"]["known_pass_rate"]), f4(opt["model_free"]["projected_knn"]["accuracy"])])
        for q in Q.values():
            d = q["deploy"]
            rows.append([q["zh"], ", ".join(q["class_keys"]), f4(d["argmax"]["accuracy"]), f4(d["argmax"]["macro_f1"]),
                         f"{d['worst_recall']['key']} {f3(d['worst_recall']['recall'])}", f4(d["final"]["macro_f1"]),
                         f"{d['worst_recall_final']['key']} {f3(d['worst_recall_final']['recall'])}",
                         f4(d["final"]["known_pass_rate"]), f4(q["model_free"]["projected_knn"]["accuracy"])])
        L += md_table(["方案", "类别", "acc", "macro-F1", "最低召回", "最终 macro-F1", "最终最低召回", "已知类通过率",
                       f"{KNN_K}NN acc"], rows)
        L.append("")
        L += gate_table(([("七类（基线）", opt["deploy"])] if opt else []) + [(q["zh"], q["deploy"]) for q in Q.values()])
        L.append("\n语义保真（单位绑定 resmlp 最终判定读投影脸；合并类读成任一成员都算对，读成已去掉的类计 other）：\n")
        rows = []
        if opt and "cross_read" in opt:
            cr = opt["cross_read"]["resmlp_final"]
            rows.append(["七类（基线）", f4(cr["accuracy"]), "，".join(f"{k} {f3(v)}" for k, v in cr["recall"].items())])
        for q in Q.values():
            if "cross_read" in q:
                cr = q["cross_read"]["resmlp_final"]
                rows.append([q["zh"], f4(cr["accuracy"]), "，".join(f"{k} {f3(v)}" for k, v in cr["recall"].items())])
        L += md_table(["方案", "acc", "逐类读对率"], rows)
        for q in Q.values():
            if q.get("dropped_variants"):
                L.append(f"\n- {q['zh']} 去掉的变体（类别: 变体下标）：{q['dropped_variants']}")
        L.append("")
        for q in Q.values():
            d = q["deploy"]
            L.append(f"\n**{q['zh']}** 逐类召回（resmlp argmax / 最终判定）：\n")
            L += md_table(["口径"] + d["class_keys"],
                          [["argmax"] + [f3(p["recall"]) for p in d["argmax"]["per_class"]],
                           ["最终"] + [f3(p["recall"]) for p in d["final"]["per_class"]]])
            L.append("\n混淆矩阵（argmax）：\n")
            L += md_table(["真值 \\ 预测"] + d["class_keys"],
                          [[k] + row for k, row in zip(d["class_keys"], d["argmax"]["confusion"])])
        L.append("")
    L.append(f"\n耗时（秒）：{S.get('timing')}\n")
    return "\n".join(L)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="laila 12 维输入七类表情可分性上限分析（一条命令重跑全部）")
    ap.add_argument("--stage", choices=["rigs", "reduced", "all"], default="all")
    ap.add_argument("--reuse", action="store_true", help="训练产物已存在就不重训")
    ap.add_argument("--epochs", type=int, default=None, help="覆盖训练 epoch（只用于试跑）")
    args = ap.parse_args(argv)
    t_all = time.time()
    OUT.mkdir(parents=True, exist_ok=True)
    base_cfg = load_config()
    canonical = load_canonical()
    labels = load_labels()
    facs_cfg = load_yaml(CONFIG_DIR / "facs.yaml")
    summary_path = OUT / "summary.json"
    S = {}
    if summary_path.exists() and args.stage != "all":
        import json

        S = json.loads(summary_path.read_text(encoding="utf-8"))
    S["note"] = "合成 FACS 先验 + 候选对应的可分性上限；两份 laila 候选不是绑定，系数未经几何校准"
    S["config"] = {"model": MODEL, "seed": base_cfg.get("seed"), "epochs": args.epochs or base_cfg["optim"]["epochs"],
                   "n_per_class": base_cfg["data"]["sources"][0].get("n_per_class"), "knn_k": KNN_K,
                   "proto_intensity": PROTO_INTENSITY, "gate_recall": GATE_RECALL}
    timing = S.get("timing", {})
    if args.stage in ("rigs", "all"):
        t0 = time.time()
        S["rigs"] = stage_rigs(base_cfg, canonical, labels, facs_cfg, args)
        timing["rigs"] = round(time.time() - t0, 1)
    if args.stage in ("reduced", "all"):
        t0 = time.time()
        S["reduced"] = stage_reduced(base_cfg, canonical, labels, facs_cfg, args)
        timing["reduced"] = round(time.time() - t0, 1)
    timing["total_this_invocation"] = round(time.time() - t_all, 1)
    S["timing"] = timing
    save_json(S, summary_path)
    (OUT / "summary.md").write_text(render_summary(S), encoding="utf-8", newline="\n")
    log(f"\n[完成] 汇总：artifacts/laila_upper_bound/summary.md（用时 {time.time() - t_all:.0f} 秒）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
