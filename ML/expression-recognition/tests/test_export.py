"""导出：数值误差、算子白名单、元数据字段、单位绑定、商用标记。"""

import json
from pathlib import Path

import numpy as np
import pytest
import torch

from exprnet.export import DeployModel, load_sentis_ops, run_export
from exprnet.models import build_model, count_params
from exprnet.rig import identity_rig

MODEL_CFG = {"regionformer": {"d_model": 48, "heads": 4, "layers": 2, "ffn_mult": 2, "dropout": 0.1},
             "resmlp": {"width": 128, "hidden": 256, "blocks": 2, "dropout": 0.1}}


def _fake_run(tmp_path: Path, canonical, labels, rig, name="regionformer", sources=None) -> Path:
    torch.manual_seed(0)
    model = build_model(name, MODEL_CFG, canonical, labels.num_classes).eval()
    run = tmp_path / "run"
    run.mkdir()
    ckpt = {
        "model_name": name, "model_cfg": MODEL_CFG[name], "state_dict": model.state_dict(), "params": count_params(model),
        "class_keys": labels.keys, "class_zh": labels.zh, "canonical_names": canonical.names,
        "masked_names": canonical.masked_names, "temperature": 1.3, "energy_threshold": -2.0, "min_confidence": 0.4,
        "threshold_rule": "tnr95", "threshold_params": {"tnr": 0.95, "tpr": 0.95},
        "energy_threshold_tnr95": -2.0, "energy_threshold_id95": -3.0,
        "train_rig": rig.to_config(), "train_rig_hash": rig.config_hash(),
        "data_sources": sources or [{"kind": "synthetic", "name": "facs_synthetic", "license": "自有"}],
        "commercial_use_allowed": all(s["kind"] == "synthetic" for s in (sources or [{"kind": "synthetic"}])),
    }
    torch.save(ckpt, run / "ckpt.pt")
    return run


def test_sentis_ops_file():
    ops = load_sentis_ops()
    assert {"MatMul", "Softmax", "Relu", "Reshape", "Transpose", "Erf", "ReduceMean"} <= ops
    assert "Loop" not in ops and "If" not in ops


@pytest.mark.parametrize("name", ["regionformer", "resmlp"])
def test_export_checks_pass(tmp_path, canonical, labels, sample_rig, name):
    run = _fake_run(tmp_path, canonical, labels, sample_rig, name)
    ok, onnx_path, json_path = run_export(run, out_dir=tmp_path / "out", name="m", n_check=1000, log=lambda *_: None)
    assert ok, json.loads(Path(json_path).read_text(encoding="utf-8"))["checks"]
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    c = meta["checks"]
    assert c["numeric"]["max_abs_err_probs"] < 1e-5 and c["numeric"]["max_abs_err_energy"] < 1e-5
    assert set(c["operators"]["used"]) <= load_sentis_ops()
    assert c["operators"]["opset_imports"] == {"ai.onnx": 15}
    assert c["size"]["bytes"] < 1024 * 1024
    assert meta["energy_threshold_rule"] == "tnr95"
    assert meta["threshold_calibration"]["thresholds"] == {"tnr95": -2.0, "id95": -3.0}
    for k in ["schema_version", "labels", "sliders", "temperature", "energy_threshold", "min_confidence",
              "canonical_dims", "masked_dims", "rig", "training_data", "commercial_use_allowed", "metrics_summary"]:
        assert k in meta, k
    assert meta["rig"]["config_hash"] == sample_rig.config_hash()
    assert [s["key"] for s in meta["sliders"]] == sample_rig.keys
    assert {"key", "zh", "min", "max", "default"} <= set(meta["sliders"][0])
    assert meta["labels"][0] == {"key": labels.keys[0], "zh": labels.zh[0]}
    assert meta["commercial_use_allowed"] is True
    io = meta["onnx"]
    assert io["inputs"][0]["name"] == "sliders" and io["inputs"][0]["shape"] == ["batch", sample_rig.num_sliders]
    assert [o["name"] for o in io["outputs"]] == ["probs", "energy"]


def test_onnx_dynamic_batch_and_semantics(tmp_path, canonical, labels, sample_rig):
    import onnxruntime as ort

    run = _fake_run(tmp_path, canonical, labels, sample_rig)
    ok, onnx_path, _ = run_export(run, out_dir=tmp_path / "out", name="m", n_check=200, log=lambda *_: None)
    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    for b in (1, 3, 17):
        p, e = sess.run(None, {"sliders": np.zeros((b, sample_rig.num_sliders), np.float32)})
        assert p.shape == (b, labels.num_classes) and e.shape == (b,)
        assert np.allclose(p.sum(-1), 1, atol=1e-5)


def test_identity_rig_export(tmp_path, canonical, labels):
    rig = identity_rig(canonical)
    run = _fake_run(tmp_path, canonical, labels, rig)
    ok, _, json_path = run_export(run, out_dir=tmp_path / "out", name="id", n_check=200, log=lambda *_: None)
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    assert ok and len(meta["sliders"]) == 51 and meta["rig"]["identity"] is True


def test_rig_change_changes_hash_in_metadata(tmp_path, canonical, labels, sample_rig):
    import yaml

    run = _fake_run(tmp_path, canonical, labels, sample_rig)
    cfg = sample_rig.to_config()
    cfg["sliders"][3]["neg"]["browInnerUp"] = 0.6
    p = tmp_path / "rig2.yaml"
    p.write_text(yaml.safe_dump(cfg, allow_unicode=True), encoding="utf-8")
    ok, _, json_path = run_export(run, rig_path=p, out_dir=tmp_path / "out", name="r2", n_check=100, log=lambda *_: None)
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    assert ok and meta["rig"]["config_hash"] != sample_rig.config_hash()
    assert meta["train_rig_hash"] == sample_rig.config_hash()


def test_commercial_flag_false_with_public_data(tmp_path, canonical, labels, sample_rig):
    srcs = [{"kind": "synthetic", "name": "facs_synthetic", "license": "自有"},
            {"kind": "npz", "name": "rafdb", "license": "仅限非商用研究"}]
    run = _fake_run(tmp_path, canonical, labels, sample_rig, sources=srcs)
    ok, _, json_path = run_export(run, out_dir=tmp_path / "out", name="pub", n_check=100, log=lambda *_: None)
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    assert meta["commercial_use_allowed"] is False
    assert [s["name"] for s in meta["training_data"]] == ["facs_synthetic", "rafdb"]


def test_deploy_matches_rig_forward(canonical, labels, sample_rig):
    torch.manual_seed(0)
    clf = build_model("resmlp", MODEL_CFG, canonical, labels.num_classes).eval()
    dm = DeployModel(sample_rig, canonical, clf, 1.0).eval()
    s = np.random.default_rng(0).uniform(-1.3, 1.3, size=(20, sample_rig.num_sliders)).astype(np.float32)
    x = sample_rig.forward(s) * canonical.mask_vector
    with torch.no_grad():
        p, e = dm(torch.from_numpy(s))
        ref = torch.softmax(clf(torch.from_numpy(x)), -1)
    assert torch.allclose(p, ref, atol=1e-5)


def test_failed_check_renames(tmp_path, canonical, labels, sample_rig, monkeypatch):
    import exprnet.export as ex

    run = _fake_run(tmp_path, canonical, labels, sample_rig)
    monkeypatch.setattr(ex, "load_sentis_ops", lambda path=None: {"MatMul"})  # 故意给一个极小白名单
    ok, onnx_path, json_path = ex.run_export(run, out_dir=tmp_path / "out", name="bad", n_check=50, log=lambda *_: None)
    assert not ok and onnx_path.name == "bad.failed.onnx" and onnx_path.exists()
    assert not (tmp_path / "out" / "bad.onnx").exists()
