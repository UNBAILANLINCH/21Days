"""历史 laila_v1 的 12 维输入契约（训练 PRP §4.1、§7.4、§15.1）。

当前 31 形态模型的拟定 17 维 v2 契约见 PRP §16；本文件通过不代表 v2 校准或训练完成。

- 契约常量照抄规格 §4.1：12 个 key、顺序、范围、default = 0、上唇 / 下唇只有 pos；
- analysis/laila_upper_bound/ 下两份候选绑定都要符合契约（它们只是分析用候选，不许冒充正式绑定 laila_v1）；
- configs/rigs/laila_rig.yaml 存在时必须完全符合契约且 name 为 laila_v1；不存在时跳过（等阶段门 B 几何校准定稿）；
- 候选绑定 + 五类类别集小规模训练 → 导出：sliders 12 维、probs 5 维、labels 顺序等于 §15.1、sha256 正确、三项自检通过。
"""

import hashlib
import json

import numpy as np
import pytest
import torch
import yaml

from exprnet.common import CONFIG_DIR, PROJECT_ROOT, portable_path
from exprnet.export import run_export
from exprnet.rig import rig_from_dict
from exprnet.train import load_config, train

# ---- 出自规格 §4.1「固定 key、顺序与读取来源」表：索引顺序即下面的列表顺序，范围照抄，全部默认值为 0。
LAILA_SLIDER_CONTRACT = [
    ("brow_L_y", -1.0, 1.0),          # 0  P(Brow_L_Up, Brow_L_Down)
    ("brow_R_y", -1.0, 1.0),          # 1  P(Brow_R_Up, Brow_R_Down)
    ("eye_L_upper_y", -1.0, 1.0),     # 2  P(Eye_L_UpperLid_Up, Eye_L_UpperLid_Down)
    ("eye_L_lower_y", -1.0, 1.0),     # 3  P(Eye_L_LowerLid_Up, Eye_L_LowerLid_Down)
    ("eye_R_upper_y", -1.0, 1.0),     # 4  P(Eye_R_UpperLid_Up, Eye_R_UpperLid_Down)
    ("eye_R_lower_y", -1.0, 1.0),     # 5  P(Eye_R_LowerLid_Up, Eye_R_LowerLid_Down)
    ("mouth_corner_L_y", -1.0, 1.0),  # 6  P(Mouth_L_Up, Mouth_L_Down)
    ("mouth_corner_R_y", -1.0, 1.0),  # 7  P(Mouth_R_Up, Mouth_R_Down)
    ("mouth_corner_L_x", -1.0, 1.0),  # 8  P(Mouth_L_Out, Mouth_L_In)
    ("mouth_corner_R_x", -1.0, 1.0),  # 9  P(Mouth_R_Out, Mouth_R_In)
    ("upper_lip", 0.0, 1.0),          # 10 W(Mouth_UpperLip_Up.001)
    ("lower_lip", 0.0, 1.0),          # 11 W(Mouth_LowerLip_Down.001)
]
LAILA_DEFAULT = 0.0                        # 规格 §4.1：全部默认值为 0
LAILA_POS_ONLY = ("upper_lip", "lower_lip")  # 规格 §4.1 / §5.1：上 / 下唇只有 pos，不写 neg
LAILA_RIG_NAME = "laila_v1"                # 规格 §4.3 / §5.1：正式绑定名
# ---- 出自规格 §15.1「验收类别集改为五类」表：顺序即 probs 顺序。
LAILA_LABELS = ["neutral", "happy", "sad", "surprise_fear", "angry"]
LAILA_LABEL_FROM = {"neutral": ["neutral"], "happy": ["happy"], "sad": ["sad"],
                    "surprise_fear": ["surprise", "fear"], "angry": ["angry"]}
LAILA_LABEL_DROP = ["disgust"]

CANDIDATE_DIR = PROJECT_ROOT / "analysis" / "laila_upper_bound"
CANDIDATES = {
    "conservative": CANDIDATE_DIR / "laila_candidate_conservative.yaml",
    "optimistic": CANDIDATE_DIR / "laila_candidate_optimistic.yaml",
}
LAILA_RIG = CONFIG_DIR / "rigs" / "laila_rig.yaml"


def assert_laila_contract(raw: dict, canonical) -> None:
    """绑定配置（yaml 原文）逐项对照规格 §4.1。"""
    sliders = raw["sliders"]
    assert [s["key"] for s in sliders] == [k for k, _, _ in LAILA_SLIDER_CONTRACT], "key 或顺序与规格 §4.1 不一致"
    rig = rig_from_dict(raw, canonical)  # 绑定本身的校验（范围含 0、权重非负、表情基存在）也要过
    assert rig.num_sliders == 12
    for s, (key, lo, hi) in zip(rig.sliders, LAILA_SLIDER_CONTRACT):
        assert (s.min, s.max) == (lo, hi), f"{key} 的范围应为 [{lo:g}, {hi:g}]，实际 [{s.min:g}, {s.max:g}]"
        assert s.default == LAILA_DEFAULT, f"{key} 的默认值应为 0"
        assert s.pos, f"{key} 没有 pos"
    for raw_s, s in zip(sliders, rig.sliders):
        if s.key in LAILA_POS_ONLY:
            assert not raw_s.get("neg") and not s.neg, f"{s.key} 只能有 pos，不能写 neg"
        assert raw_s.get("default", 0) == 0
    x0 = rig.forward(np.zeros((1, 12)))
    assert np.all(x0 == 0), "全零输入必须得到全零规范向量"


@pytest.mark.parametrize("which", sorted(CANDIDATES))
def test_candidate_rigs_follow_contract(which, canonical):
    raw = yaml.safe_load(CANDIDATES[which].read_text(encoding="utf-8"))
    assert_laila_contract(raw, canonical)
    assert raw["name"] != LAILA_RIG_NAME, "分析用候选不许冒充正式绑定 laila_v1"


def test_formal_laila_rig_follows_contract(canonical):
    if not LAILA_RIG.exists():
        pytest.skip("configs/rigs/laila_rig.yaml 尚不存在：等阶段门 B 几何校准定稿（规格 §5、§15.3）")
    raw = yaml.safe_load(LAILA_RIG.read_text(encoding="utf-8"))
    assert raw["name"] == LAILA_RIG_NAME
    assert_laila_contract(raw, canonical)


def test_label_set_matches_spec():
    raw = yaml.safe_load((CONFIG_DIR / "label_sets" / "laila_5class.yaml").read_text(encoding="utf-8"))
    assert [c["key"] for c in raw["classes"]] == LAILA_LABELS
    assert {c["key"]: c["from"] for c in raw["classes"]} == LAILA_LABEL_FROM
    assert raw["drop"] == LAILA_LABEL_DROP


def test_candidate_5class_train_export(tmp_path):
    """乐观候选 + 五类类别集 + 变体剔除，小规模训练 → 导出（规格 §7.4 按 §15.2 改为 probs [batch,5]）。"""
    import onnx
    import onnxruntime as ort

    cfg = load_config()
    cfg["model"]["name"] = "resmlp"
    cfg["rig"] = portable_path(CANDIDATES["optimistic"])
    cfg["label_set"] = "configs/label_sets/laila_5class.yaml"
    cfg["infeasible_variants"] = {"drop": True, "rel_residual_threshold": 0.9, "probe_intensity": 0.6}
    cfg["golden"] = None
    cfg["data"]["sources"] = [{"kind": "synthetic", "n_per_class": 60, "seed": 3}]
    cfg["data"]["negatives"] = {"train": 300, "heldout": 150, "calib": None}
    cfg["optim"]["epochs"] = 2
    cfg["optim"]["batch_size"] = 64
    run = train(cfg, "laila_cand_contract", out_root=tmp_path / "runs", log=lambda *_: None, use_cache=False)

    ckpt = torch.load(run / "ckpt.pt", weights_only=False)
    assert ckpt["class_keys"] == LAILA_LABELS
    assert ckpt["label_set"]["name"] == "laila_5class"
    dropped = {(r["emotion"], r["variant"], r["choose"]) for r in ckpt["variant_filter"]["dropped"]}
    assert ("angry", 2, None) in dropped  # CK+ 最小判据 AU23+24：乐观候选没有抿唇控制，做不出来
    assert "训练目标剔除的变体" in (run / "report.md").read_text(encoding="utf-8")

    ok, onnx_path, json_path = run_export(run, out_dir=tmp_path / "export", name="laila_cand_contract", n_check=1000,
                                          log=lambda *_: None)
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    assert ok, meta["checks"]
    assert all(meta["checks"][k]["ok"] for k in ("numeric", "operators", "size"))

    proto = onnx.load(str(onnx_path))
    dims = lambda v: [d.dim_param or d.dim_value for d in v.type.tensor_type.shape.dim]  # noqa: E731
    io = {v.name: dims(v) for v in list(proto.graph.input) + list(proto.graph.output)}
    assert io["sliders"] == ["batch", 12] and io["probs"] == ["batch", 5] and io["energy"] == ["batch"]
    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    p, e = sess.run(None, {"sliders": np.zeros((3, 12), np.float32)})
    assert p.shape == (3, 5) and e.shape == (3,)

    assert [s["key"] for s in meta["sliders"]] == [k for k, _, _ in LAILA_SLIDER_CONTRACT]
    assert [(s["min"], s["max"], s["default"]) for s in meta["sliders"]] == [(lo, hi, 0.0) for _, lo, hi in LAILA_SLIDER_CONTRACT]
    assert [lb["key"] for lb in meta["labels"]] == LAILA_LABELS
    assert meta["label_set"]["name"] == "laila_5class"
    assert {c["key"]: c["from"] for c in meta["label_set"]["classes"]} == LAILA_LABEL_FROM
    assert meta["label_set"]["drop"] == LAILA_LABEL_DROP
    assert meta["onnx"]["sha256"] == hashlib.sha256(onnx_path.read_bytes()).hexdigest()
    assert meta["rig"]["name"] != LAILA_RIG_NAME  # 候选训出来的模型不是正式模型
    assert meta["training_variant_filter"]["dropped"]
