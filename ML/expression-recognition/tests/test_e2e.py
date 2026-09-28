"""端到端冒烟：合成数据训 2 个 epoch → 校准 → 金标评估 → 导出。几十秒内跑完，不写 .cache。"""

import json

import pytest

from exprnet.common import PROJECT_ROOT
from exprnet.export import run_export
from exprnet.train import load_config, train


@pytest.mark.parametrize("model", ["regionformer", "resmlp"])
def test_end_to_end(tmp_path, model):
    cfg = load_config()
    cfg["model"]["name"] = model
    cfg["data"]["sources"] = [{"kind": "synthetic", "n_per_class": 60, "seed": 3}]
    cfg["data"]["negatives"] = {"train": 300, "heldout": 150, "calib": None}
    cfg["optim"]["epochs"] = 2
    cfg["optim"]["batch_size"] = 64
    cfg["golden"] = str(PROJECT_ROOT / "golden" / "sample_rig.json")
    run = train(cfg, f"e2e_{model}", out_root=tmp_path / "runs", log=lambda *_: None, use_cache=False)

    for f in ["ckpt.pt", "config.yaml", "metrics.json", "report.md", "eval_data.npz"]:
        assert (run / f).exists(), f
    m = json.loads((run / "metrics.json").read_text(encoding="utf-8"))
    for k in ["accuracy", "macro_f1", "per_class", "confusion"]:
        assert k in m["val"]
    assert 0 <= m["calibration"]["ece_after"] <= 1
    assert 0 <= m["ood"]["auroc"] <= 1 and 0 <= m["ood"]["fpr95"] <= 1
    assert m["golden"]["n"] >= 25 and len(m["golden"]["entries"]) == m["golden"]["n"]
    th = m["thresholds"]
    assert th["rule"] == "tnr95" and set(th["by_rule"]) == {"tnr95", "id95"}
    assert m["ood"]["energy_threshold"] == th["by_rule"]["tnr95"]["threshold"]
    assert th["by_rule"]["tnr95"]["calib_neg_reject_rate"] == pytest.approx(0.95, abs=0.02)  # 标定集按构造
    assert 0.0 <= th["by_rule"]["tnr95"]["heldout_neg_reject_rate"] <= 1.0                  # 留出集是独立检验
    assert set(th["neg_sets"]) == {"calib", "heldout"}
    assert th["by_rule"]["id95"]["id_pass_rate"] == pytest.approx(0.95, abs=0.02)
    assert "golden_known_pass" in th["by_rule"]["tnr95"]
    assert m["commercial_use_allowed"] is True
    assert "不能证明泛化" in (run / "report.md").read_text(encoding="utf-8")

    ok, onnx_path, json_path = run_export(run, out_dir=tmp_path / "export", n_check=1000, log=lambda *_: None)
    assert ok
    meta = json.loads(json_path.read_text(encoding="utf-8"))
    assert meta["commercial_use_allowed"] is True
    assert meta["metrics_summary"]["golden_accuracy"] is not None
    tc = meta["threshold_calibration"]
    assert meta["energy_threshold_rule"] == "tnr95" and "stats_by_rule" in tc
    assert set(tc["neg_sets"]) == {"calib", "heldout"} and tc["neg_sets"]["calib"]["seed"] != tc["neg_sets"]["heldout"]["seed"]
    assert {"calib_neg_reject_rate", "heldout_neg_reject_rate", "heldout_neg_reject_rate_with_min_conf"} <= set(tc["field_notes"])
