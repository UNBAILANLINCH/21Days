"""导出 ONNX + 元数据并自检（DESIGN §8）。

图内依次做：滑杆裁剪到范围 → 正 / 负半轴拆分（Relu）→ 乘绑定矩阵得到规范空间（再裁剪到 [0,1]）
→ 屏蔽维置零 → 分类网络 → 除以 T → softmax 与 energy。
energy = −T·(max(z) + log Σ exp(z − max(z)))，z = logits / T，手动展开 logsumexp（数值稳定、不依赖运行时实现）。

三项自检，任一不过就判导出失败（产物改名为 *.failed.*，进程返回非 0）：
    1. onnxruntime 与 PyTorch 在 1000 个随机输入上的最大绝对误差 < 1e-5
    2. 图里所有算子都在 configs/sentis_ops.txt 里，且 opset 为 15、无自定义域
    3. 文件 < 1 MB

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.export --run artifacts/runs/rf_synth
    python -m exprnet.export --run artifacts/runs/rf_synth --rig configs/rigs/my_rig.yaml --name rf_my_rig
"""

from __future__ import annotations

import datetime as _dt
import sys
import warnings
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

from .canonical import Canonical
from .common import make_parser, ARTIFACTS_DIR, CONFIG_DIR, load_json, rel_to_project, resolve_cli_path, save_json
from .rig import Rig, identity_rig, load_rig, rig_from_dict

SCHEMA_VERSION = 1
OPSET = 15
MAX_BYTES = 1024 * 1024
MAX_ABS_ERR = 1e-5


class DeployModel(nn.Module):
    """滑杆 → probs / energy 的完整推理图（训练用的 rig.forward 与这里是同一套公式）。"""

    def __init__(self, rig: Rig, canonical: Canonical, classifier: nn.Module, temperature: float):
        super().__init__()
        self.register_buffer("s_min", torch.as_tensor(rig.s_min, dtype=torch.float32).view(1, -1))
        self.register_buffer("s_max", torch.as_tensor(rig.s_max, dtype=torch.float32).view(1, -1))
        self.register_buffer("w_pos", torch.as_tensor(rig.W_pos, dtype=torch.float32))
        self.register_buffer("w_neg", torch.as_tensor(rig.W_neg, dtype=torch.float32))
        self.register_buffer("mask", torch.as_tensor(canonical.mask_vector, dtype=torch.float32).view(1, -1))
        self.register_buffer("temperature", torch.tensor(float(temperature), dtype=torch.float32))
        self.has_neg = bool(np.any(rig.W_neg != 0))
        self.classifier = classifier

    def forward(self, sliders: torch.Tensor):
        s = torch.minimum(torch.maximum(sliders, self.s_min), self.s_max)
        x = torch.matmul(torch.relu(s), self.w_pos)
        if self.has_neg:
            x = x + torch.matmul(torch.relu(-s), self.w_neg)
        x = torch.clamp(x, 0.0, 1.0)
        x = x * self.mask
        logits = self.classifier(x)
        z = logits / self.temperature
        probs = torch.softmax(z, dim=-1)
        m = torch.amax(z, dim=-1, keepdim=True)
        lse = m.squeeze(-1) + torch.log(torch.sum(torch.exp(z - m), dim=-1))
        energy = -self.temperature * lse
        return probs, energy


def load_sentis_ops(path: str | Path | None = None) -> set[str]:
    path = Path(path) if path else CONFIG_DIR / "sentis_ops.txt"
    ops = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            ops.add(line)
    return ops


def graph_ops(model_proto) -> set[str]:
    """收集图（含子图）里的全部算子类型。"""
    import onnx

    ops: set[str] = set()

    def walk(g):
        for node in g.node:
            ops.add(node.op_type)
            for a in node.attribute:
                if a.type == onnx.AttributeProto.GRAPH:
                    walk(a.g)
                elif a.type == onnx.AttributeProto.GRAPHS:
                    for sg in a.graphs:
                        walk(sg)

    walk(model_proto.graph)
    return ops


def random_slider_inputs(rig: Rig, n: int, seed: int = 0) -> np.ndarray:
    """自检用随机输入：各滑杆在范围两端各外扩 20%（顺带检查裁剪），另有 5% 全零行。"""
    rng = np.random.default_rng(seed)
    span = rig.s_max - rig.s_min
    lo, hi = rig.s_min - 0.2 * span, rig.s_max + 0.2 * span
    x = rng.uniform(lo, hi, size=(n, rig.num_sliders))
    x[rng.random(n) < 0.05] = 0.0
    return x.astype(np.float32)


def export_onnx(deploy: DeployModel, rig: Rig, out_path: Path, n_check: int = 1000, sentis_ops: set[str] | None = None) -> dict:
    """导出并做三项自检，返回检查结果字典。"""
    import onnx
    import onnxruntime as ort

    out_path.parent.mkdir(parents=True, exist_ok=True)
    deploy.eval()
    dummy = torch.zeros(2, rig.num_sliders, dtype=torch.float32)
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")  # 旧版 TorchScript 导出器的弃用提示；opset 15 需要走它
        torch.onnx.export(
            deploy, (dummy,), str(out_path),
            input_names=["sliders"], output_names=["probs", "energy"],
            opset_version=OPSET, do_constant_folding=True,
            dynamic_axes={"sliders": {0: "batch"}, "probs": {0: "batch"}, "energy": {0: "batch"}},
            dynamo=False,
        )
    model_proto = onnx.load(str(out_path))
    onnx.checker.check_model(model_proto)

    # 1. 数值一致
    xs = random_slider_inputs(rig, n_check)
    with torch.no_grad():
        p_t, e_t = deploy(torch.from_numpy(xs))
    sess = ort.InferenceSession(str(out_path), providers=["CPUExecutionProvider"])
    p_o, e_o = sess.run(["probs", "energy"], {"sliders": xs})
    err_p = float(np.abs(p_o - p_t.numpy()).max())
    err_e = float(np.abs(e_o - e_t.numpy()).max())
    shape_ok = p_o.shape == (n_check, p_t.shape[1]) and e_o.shape == (n_check,)
    num_ok = max(err_p, err_e) < MAX_ABS_ERR and shape_ok

    # 2. 算子白名单
    ops = graph_ops(model_proto)
    allowed = sentis_ops if sentis_ops is not None else load_sentis_ops()
    outside = sorted(ops - allowed)
    opsets = {(o.domain or "ai.onnx"): o.version for o in model_proto.opset_import}
    opset_ok = opsets.get("ai.onnx") == OPSET and set(opsets) == {"ai.onnx"}
    ops_ok = not outside and opset_ok

    # 3. 大小
    size = out_path.stat().st_size
    size_ok = size < MAX_BYTES

    io = {
        "inputs": [{"name": i.name, "shape": [d.dim_param or d.dim_value for d in i.type.tensor_type.shape.dim]} for i in model_proto.graph.input],
        "outputs": [{"name": o.name, "shape": [d.dim_param or d.dim_value for d in o.type.tensor_type.shape.dim]} for o in model_proto.graph.output],
    }
    return {
        "ok": bool(num_ok and ops_ok and size_ok),
        "numeric": {"ok": bool(num_ok), "n_inputs": n_check, "max_abs_err_probs": err_p, "max_abs_err_energy": err_e,
                    "threshold": MAX_ABS_ERR, "shape_ok": bool(shape_ok)},
        "operators": {"ok": bool(ops_ok), "used": sorted(ops), "outside_whitelist": outside, "opset_imports": opsets,
                      "whitelist": "configs/sentis_ops.txt"},
        "size": {"ok": bool(size_ok), "bytes": int(size), "limit_bytes": MAX_BYTES},
        "io": io,
    }


def build_metadata(name: str, ckpt: dict, rig: Rig, canonical: Canonical, checks: dict, metrics: dict | None, onnx_file: str) -> dict:
    train_rig_hash = ckpt.get("train_rig_hash")
    rig_hash = rig.config_hash()
    summary = {}
    if metrics:
        summary = {
            "val_accuracy": metrics.get("val", {}).get("accuracy"),
            "val_macro_f1": metrics.get("val", {}).get("macro_f1"),
            "ece_before": metrics.get("calibration", {}).get("ece_before"),
            "ece_after": metrics.get("calibration", {}).get("ece_after"),
            "ood_auroc": metrics.get("ood", {}).get("auroc"),
            "ood_fpr95": metrics.get("ood", {}).get("fpr95"),
            "golden_accuracy": (metrics.get("golden") or {}).get("accuracy"),
            "golden_known_correct": (metrics.get("golden") or {}).get("known_correct"),
            "golden_n_known": (metrics.get("golden") or {}).get("n_known"),
            "golden_weird_correct": (metrics.get("golden") or {}).get("weird_correct"),
            "golden_n_weird": (metrics.get("golden") or {}).get("n_weird"),
            "evaluated_with_rig_hash": train_rig_hash,
        }
        if train_rig_hash != rig_hash:
            summary["note"] = "导出用的绑定与训练 / 评估时不同，以上指标只反映训练时的绑定；换绑后请用对应金标集重评"
        if not ckpt.get("commercial_use_allowed", False):
            summary["caveat"] = "含公开集数据，指标只说明原型能跑"
        else:
            summary["caveat"] = "只用合成数据：验证集与训练集同源、金标由同一套 FACS 先验手写，指标只证明流程跑通，不能证明泛化"
    return {
        "schema_version": SCHEMA_VERSION,
        "name": name,
        "created": _dt.date.today().isoformat(),
        "onnx": {"file": onnx_file, "opset": OPSET, "inputs": checks["io"]["inputs"], "outputs": checks["io"]["outputs"],
                 "notes": "sliders 按下面 sliders 列表的顺序排列；probs 已做温度校准；energy 越大越不像任何表情"},
        "model": {"arch": ckpt["model_name"], "config": ckpt["model_cfg"], "params": ckpt.get("params")},
        "labels": [{"key": k, "zh": z} for k, z in zip(ckpt["class_keys"], ckpt["class_zh"])],
        "sliders": rig.slider_meta(),
        "temperature": float(ckpt["temperature"]),
        "energy_threshold": float(ckpt["energy_threshold"]),
        "energy_threshold_rule": ckpt.get("threshold_rule", "id95"),
        "threshold_calibration": _threshold_block(ckpt, metrics),
        "min_confidence": float(ckpt["min_confidence"]),
        "decision_rule": "energy > energy_threshold 或 max(probs) < min_confidence 时判「认不出」，否则取 argmax(probs)",
        "canonical_dims": list(canonical.names),
        "masked_dims": canonical.masked_names,
        "rig": {"name": rig.name, "identity": rig.identity, "config_hash": rig_hash, "hash_algorithm": "sha256(规范化后的绑定配置 + 规范空间维度顺序)"},
        "train_rig_hash": train_rig_hash,
        "training_data": ckpt.get("data_sources", []),
        "commercial_use_allowed": bool(ckpt.get("commercial_use_allowed", False)),
        "metrics_summary": summary,
        "checks": {k: v for k, v in checks.items() if k != "io"},
    }


def _threshold_block(ckpt: dict, metrics: dict | None) -> dict:
    """阈值标定说明：当前规则、各规则阈值，以及（有评估结果时）各阈值下的分布内通过率、负样本拒识率、金标通过率。"""
    rule = ckpt.get("threshold_rule", "id95")
    desc = {
        "tnr95": "留出负样本（怪脸）95% 被拒：取负样本 energy 的第 5 百分位（DESIGN §6）",
        "id95": "分布内验证集 95% 通过（旧规则，仅作对照）",
    }
    out = {"rule": rule, "description": desc.get(rule, rule), "params": ckpt.get("threshold_params"),
           "thresholds": {r: ckpt.get(f"energy_threshold_{r}") for r in ("tnr95", "id95") if ckpt.get(f"energy_threshold_{r}") is not None}}
    out["neg_sets"] = {k: {"n": v.get("n"), "seed": v.get("seed"), "use": v.get("use")}
                       for k, v in (ckpt.get("negatives") or {}).items() if k in ("calib", "heldout")}
    th = (metrics or {}).get("thresholds")
    if th:
        out["stats_by_rule"] = th["by_rule"]
        out["field_notes"] = {
            "calib_neg_reject_rate": "标定集（定 TNR95 阈值用的那批负样本）上的拒识率，按构造约为 95%",
            "heldout_neg_reject_rate": "留出集（不参与训练与定阈值）上单靠 energy 的拒识率",
            "heldout_neg_reject_rate_with_min_conf": "留出集上 energy 或 min_confidence 任一触发即拒的拒识率",
            "id_pass_rate": "分布内验证集单靠 energy 的通过率",
            "id_pass_rate_with_min_conf": "分布内验证集两道关都通过的比例",
        }
        out["note"] = th["note"]
    return out


def run_export(run_dir: Path, rig_path: Path | None = None, name: str | None = None, out_dir: Path | None = None,
               n_check: int = 1000, use_train_rig: bool = True, log=print) -> tuple[bool, Path, Path]:
    from .models import load_checkpoint

    classifier, ckpt, canonical = load_checkpoint(run_dir)
    if rig_path is not None:
        rig = load_rig(rig_path, canonical)
    elif use_train_rig and ckpt.get("train_rig") is not None:
        rig = identity_rig(canonical) if ckpt["train_rig"].get("identity") else rig_from_dict(ckpt["train_rig"], canonical)
    else:
        rig = identity_rig(canonical)
    name = name or f"{Path(run_dir).name}_{rig.name}"
    out_dir = out_dir or ARTIFACTS_DIR / "export"
    onnx_path = out_dir / f"{name}.onnx"
    json_path = out_dir / f"{name}.json"
    for p in (onnx_path, json_path, onnx_path.with_suffix(".failed.onnx"), json_path.with_suffix(".failed.json")):
        if p.exists():
            p.unlink()
    deploy = DeployModel(rig, canonical, classifier, ckpt["temperature"])
    log(f"[导出] 模型 {ckpt['model_name']}，绑定 {rig.name}（{rig.num_sliders} 根滑杆），opset {OPSET}")
    checks = export_onnx(deploy, rig, onnx_path, n_check=n_check)
    metrics_file = Path(run_dir) / "metrics.json"
    metrics = load_json(metrics_file) if metrics_file.exists() else None
    meta = build_metadata(name, ckpt, rig, canonical, checks, metrics, onnx_path.name)
    save_json(meta, json_path)

    n = checks["numeric"]; o = checks["operators"]; s = checks["size"]
    log(f"[自检 1] 数值一致：{'通过' if n['ok'] else '失败'}  probs 最大误差 {n['max_abs_err_probs']:.3e}，"
        f"energy 最大误差 {n['max_abs_err_energy']:.3e}（阈值 {MAX_ABS_ERR:g}，{n['n_inputs']} 个随机输入）")
    log(f"[自检 2] 算子白名单：{'通过' if o['ok'] else '失败'}  用到 {len(o['used'])} 种：{', '.join(o['used'])}")
    if o["outside_whitelist"]:
        log(f"          清单外算子：{', '.join(o['outside_whitelist'])}")
    log(f"          opset：{o['opset_imports']}")
    log(f"[自检 3] 文件大小：{'通过' if s['ok'] else '失败'}  {s['bytes'] / 1024:.1f} KB（上限 1024 KB）")
    if not checks["ok"]:
        f_onnx = onnx_path.with_suffix(".failed.onnx")
        f_json = json_path.with_suffix(".failed.json")
        onnx_path.rename(f_onnx)
        json_path.rename(f_json)
        log(f"[导出] 失败：产物已改名为 {f_onnx.name} / {f_json.name}，不要交给 Unity 侧")
        return False, f_onnx, f_json
    log(f"[导出] 成功：{rel_to_project(onnx_path)}、{rel_to_project(json_path)}")
    return True, onnx_path, json_path


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.export", "把训练产物导出成 ONNX（opset 15）+ 元数据 json，并做三项自检。")
    ap.add_argument("--run", required=True, help="训练产物目录，如 artifacts/runs/rf_synth")
    ap.add_argument("--rig", default=None, help="绑定配置 yaml；不给则用训练时的绑定。换绑不用重训，重新导出即可")
    ap.add_argument("--identity", action="store_true", help="导出单位绑定版本（输入直接是规范空间 51 维）")
    ap.add_argument("--name", default=None, help="导出文件名（不含扩展名），默认 <run 名>_<绑定名>")
    ap.add_argument("--out", default=None, help="输出目录，默认 artifacts/export/")
    ap.add_argument("--n-check", type=int, default=1000, help="数值自检的随机输入个数（默认 1000）")
    args = ap.parse_args(argv)
    ok, _, _ = run_export(
        resolve_cli_path(args.run), rig_path=resolve_cli_path(args.rig), name=args.name,
        out_dir=resolve_cli_path(args.out) if args.out else None, n_check=args.n_check, use_train_rig=not args.identity,
    )
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
