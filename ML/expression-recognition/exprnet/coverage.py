"""绑定覆盖度报告（DESIGN §3.2、§9）：示例 / 真实绑定够不到哪些表情、哪些表情基，反馈给捏脸设计。

做法：把样本（合成原型和 / 或公开集特征）投影到绑定可达子空间（有界 NNLS），统计残差：
    - 逐维：有没有滑杆驱动、数据里的平均激活、投影后保留下来的比例
    - 逐类：平均相对残差 ||x − x̂||² / ||x||²
    - 逐滑杆：使用率、顶到范围边界的比例（顶满说明范围不够）
被屏蔽的维（默认 eyeLook*）不参与。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.coverage --rig configs/rigs/sample_rig.yaml
    python -m exprnet.coverage --rig configs/rigs/sample_rig.yaml --npz data/features/ckplus.npz
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np

from .canonical import Canonical, load_canonical
from .common import make_parser, ARTIFACTS_DIR, CONFIG_DIR, load_labels, rel_to_project, resolve_cli_path, save_json
from .datasets import apply_baseline, load_features, neutral_baseline
from .facs import FacsSynthesizer
from .rig import Rig, load_rig

ACTIVE_EPS = 0.02  # 平均激活低于它的维视为「数据里基本不用」


def coverage_report(rig: Rig, canonical: Canonical, X: np.ndarray, y: np.ndarray | None, class_keys: list[str],
                    source_desc: str) -> dict:
    mask = canonical.mask_vector
    S, Xh = rig.project(X, mask)
    Xm = X * mask
    R = Xm - Xh
    reach = rig.reachable()
    drivers: dict[str, list[str]] = {n: [] for n in canonical.names}
    for k, s in enumerate(rig.sliders):
        for n, w in s.pos.items():
            drivers[n].append(f"{s.key}+({w:g})")
        for n, w in s.neg.items():
            drivers[n].append(f"{s.key}−({w:g})")

    dims = []
    for j, n in enumerate(canonical.names):
        if canonical.masked[j]:
            continue
        tot = float(np.abs(Xm[:, j]).sum())
        dims.append({
            "name": n,
            "reachable": bool(reach[j]),
            "drivers": drivers[n],
            "mean_activation": float(Xm[:, j].mean()),
            "mean_abs_residual": float(np.abs(R[:, j]).mean()),
            "kept_ratio": float(1.0 - np.abs(R[:, j]).sum() / tot) if tot > 1e-9 else None,
        })
    norm2 = (Xm**2).sum(1)
    rel = np.where(norm2 > 1e-6, (R**2).sum(1) / np.maximum(norm2, 1e-6), 0.0)
    per_class = {}
    if y is not None:
        for c, k in enumerate(class_keys):
            m = y == c
            if m.any():
                per_class[k] = {"n": int(m.sum()), "mean_rel_residual": float(rel[m].mean()),
                                "p90_rel_residual": float(np.quantile(rel[m], 0.9))}
    sliders = []
    for k, s in enumerate(rig.sliders):
        v = S[:, k]
        sliders.append({"key": s.key, "zh": s.zh, "used_rate": float((np.abs(v) > 0.02).mean()),
                        "at_max_rate": float((v >= s.max - 1e-3).mean()) if s.max > 0 else 0.0,
                        "at_min_rate": float((v <= s.min + 1e-3).mean()) if s.min < 0 else 0.0,
                        "mean_abs": float(np.abs(v).mean())})
    unreachable_used = [d["name"] for d in dims if not d["reachable"] and d["mean_activation"] >= ACTIVE_EPS]
    unreachable_all = [d["name"] for d in dims if not d["reachable"]]
    poorly = [d["name"] for d in dims if d["reachable"] and d["kept_ratio"] is not None and d["kept_ratio"] < 0.7
              and d["mean_activation"] >= ACTIVE_EPS]
    return {
        "rig": {"name": rig.name, "config_hash": rig.config_hash(), "num_sliders": rig.num_sliders},
        "data": source_desc,
        "n_samples": int(len(X)),
        "masked_dims": canonical.masked_names,
        "summary": {
            "mean_rel_residual": float(rel.mean()),
            "p50_rel_residual": float(np.quantile(rel, 0.5)),
            "p90_rel_residual": float(np.quantile(rel, 0.9)),
            "unreachable_dims": unreachable_all,
            "unreachable_but_used": unreachable_used,
            "poorly_covered_dims": poorly,
        },
        "per_class": per_class,
        "dims": dims,
        "sliders": sliders,
    }


def render_markdown(rep: dict) -> str:
    s = rep["summary"]
    L = [f"# 绑定覆盖度报告：{rep['rig']['name']}\n"]
    L.append(f"- 绑定哈希 `{rep['rig']['config_hash'][:16]}…`，{rep['rig']['num_sliders']} 根滑杆")
    L.append(f"- 数据：{rep['data']}，共 {rep['n_samples']} 个样本；屏蔽维（不参与）：{', '.join(rep['masked_dims']) or '无'}")
    L.append(f"- 相对残差 ||x−x̂||²/||x||²：均值 {s['mean_rel_residual']:.4f}，中位数 {s['p50_rel_residual']:.4f}，P90 {s['p90_rel_residual']:.4f}\n")
    L.append("## 够不到的表情基\n")
    L.append(f"- 没有任何滑杆驱动：{', '.join(s['unreachable_dims']) or '无'}")
    L.append(f"- 其中数据里确实用到（平均激活 ≥ {ACTIVE_EPS}）：{', '.join(s['unreachable_but_used']) or '无'}")
    L.append(f"- 有滑杆但覆盖差（投影后保留 < 70%）：{', '.join(s['poorly_covered_dims']) or '无'}\n")
    if rep["per_class"]:
        L.append("## 逐类残差（越大说明这个表情越捏不出来）\n")
        L.append("| 类别 | 样本数 | 平均相对残差 | P90 |\n| --- | --- | --- | --- |")
        for k, v in sorted(rep["per_class"].items(), key=lambda kv: -kv[1]["mean_rel_residual"]):
            L.append(f"| {k} | {v['n']} | {v['mean_rel_residual']:.4f} | {v['p90_rel_residual']:.4f} |")
        L.append("")
    L.append("## 逐维\n")
    L.append("| 表情基 | 可达 | 驱动滑杆 | 平均激活 | 平均绝对残差 | 保留比例 |\n| --- | --- | --- | --- | --- | --- |")
    for d in sorted(rep["dims"], key=lambda d: -d["mean_abs_residual"]):
        kr = "—" if d["kept_ratio"] is None else f"{d['kept_ratio']:.3f}"
        L.append(f"| {d['name']} | {'是' if d['reachable'] else '**否**'} | {', '.join(d['drivers']) or '—'} | "
                 f"{d['mean_activation']:.4f} | {d['mean_abs_residual']:.4f} | {kr} |")
    L.append("\n## 逐滑杆\n")
    L.append("| 滑杆 | 中文名 | 使用率 | 顶到最大 | 顶到最小 | 平均 |值| |\n| --- | --- | --- | --- | --- | --- |")
    for sl in rep["sliders"]:
        L.append(f"| {sl['key']} | {sl['zh']} | {sl['used_rate']:.3f} | {sl['at_max_rate']:.3f} | {sl['at_min_rate']:.3f} | {sl['mean_abs']:.3f} |")
    L.append("")
    return "\n".join(L)


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.coverage", "绑定覆盖度报告：把样本投影到绑定可达子空间，统计哪些表情 / 表情基捏不出来。")
    ap.add_argument("--rig", default=str(CONFIG_DIR / "rigs" / "sample_rig.yaml"), help="绑定配置 yaml（默认示例绑定）")
    ap.add_argument("--synthetic-per-class", type=int, default=500, help="合成原型每类样本数（默认 500；0 表示不用合成数据）")
    ap.add_argument("--seed", type=int, default=0, help="合成数据种子")
    ap.add_argument("--npz", nargs="*", default=[], help="公开集特征缓存 npz（做基线归一化后参与统计）")
    ap.add_argument("--out", default=None, help="输出目录，默认 artifacts/coverage/<绑定名>/")
    args = ap.parse_args(argv)

    canonical = load_canonical()
    labels = load_labels()
    rig = load_rig(resolve_cli_path(args.rig), canonical)
    xs, ys, desc = [], [], []
    if args.synthetic_per_class > 0:
        syn = FacsSynthesizer(canonical, labels.keys)
        X, y = syn.generate(args.synthetic_per_class, args.seed)
        xs.append(X); ys.append(y); desc.append(f"FACS 合成原型（每类 {args.synthetic_per_class}）")
    neutral_idx = labels.keys.index("neutral") if "neutral" in labels.keys else None
    for p in args.npz:
        fs = load_features(resolve_cli_path(p), canonical)
        idx = np.array([labels.to_class_index(l) if l else None for l in fs.label], dtype=object)
        keep = np.array([v is not None for v in idx])
        y = np.array(list(idx[keep]), dtype=np.int64)
        X = apply_baseline(fs.x[keep], neutral_baseline(fs.x[keep], y, neutral_idx))
        xs.append(X); ys.append(y); desc.append(f"{fs.dataset}（{len(X)}）")
    if not xs:
        ap.error("没有数据：至少给合成数据或一个 --npz")
    rep = coverage_report(rig, canonical, np.concatenate(xs), np.concatenate(ys), labels.keys, " + ".join(desc))
    out = Path(resolve_cli_path(args.out)) if args.out else ARTIFACTS_DIR / "coverage" / rig.name
    out.mkdir(parents=True, exist_ok=True)
    save_json(rep, out / "coverage.json")
    (out / "coverage.md").write_text(render_markdown(rep), encoding="utf-8", newline="\n")
    s = rep["summary"]
    print(f"[覆盖度] 绑定 {rig.name}：{rep['n_samples']} 个样本，平均相对残差 {s['mean_rel_residual']:.4f}（P90 {s['p90_rel_residual']:.4f}）")
    print(f"[覆盖度] 没有滑杆驱动的表情基：{', '.join(s['unreachable_dims']) or '无'}")
    print(f"[覆盖度] 其中数据里用到的：{', '.join(s['unreachable_but_used']) or '无'}")
    print(f"[覆盖度] 覆盖差（保留 < 70%）：{', '.join(s['poorly_covered_dims']) or '无'}")
    for k, v in sorted(rep["per_class"].items(), key=lambda kv: -kv[1]["mean_rel_residual"]):
        print(f"         {k:<10} 平均相对残差 {v['mean_rel_residual']:.4f}")
    print(f"[覆盖度] 报告：{rel_to_project(out / 'coverage.md')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
