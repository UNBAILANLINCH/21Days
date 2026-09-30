"""评估器护栏（规格 §6.3、§10、§11.2）：绑定不符报错、数据护栏、状态标注、最终判定口径、类别集映射、--tag、golden_check。"""

import copy
import json
from pathlib import Path

import numpy as np
import pytest
import torch

from exprnet.common import PROJECT_ROOT, load_label_set
from exprnet.evaluate import evaluate_golden, evaluate_run, open_set_metrics, output_names
from exprnet.golden_check import GoldenError, main as golden_check_main
from exprnet.models import build_model, count_params

MODEL_CFG = {"resmlp": {"width": 32, "hidden": 64, "blocks": 1, "dropout": 0.0}}
SAMPLE_GOLDEN = PROJECT_ROOT / "golden" / "sample_rig.json"


def _fake_run(tmp_path: Path, canonical, keys, zh, rig, label_set=None) -> tuple[Path, dict, torch.nn.Module]:
    """随机初始化的小模型 + 最小 eval_data，够 evaluate_run 走完全流程（指标数值没有意义）。"""
    torch.manual_seed(0)
    model = build_model("resmlp", MODEL_CFG, canonical, len(keys)).eval()
    run = tmp_path / "run"
    run.mkdir(exist_ok=True)
    ckpt = {
        "model_name": "resmlp", "model_cfg": MODEL_CFG["resmlp"], "state_dict": model.state_dict(), "params": count_params(model),
        "class_keys": list(keys), "class_zh": list(zh), "label_set": label_set, "variant_filter": {"enabled": False},
        "canonical_names": canonical.names, "masked_names": canonical.masked_names,
        "temperature": 1.0, "energy_threshold": 100.0, "min_confidence": 0.0,  # 阈值放到极宽：不拒识，便于断言
        "threshold_rule": "tnr95", "threshold_params": {"tnr": 0.95, "tpr": 0.95},
        "energy_threshold_tnr95": 100.0, "energy_threshold_id95": 100.0,
        "train_rig": rig.to_config(), "train_rig_hash": rig.config_hash(),
        "data_sources": [{"kind": "synthetic", "name": "facs_synthetic", "license": "自有", "n_train": 1, "n_val": 1, "split": "random"}],
        "negatives": {}, "commercial_use_allowed": True, "seed": 0,
    }
    torch.save(ckpt, run / "ckpt.pt")
    rng = np.random.default_rng(0)
    n, d = 20, canonical.dim
    x = rng.random((n, d)).astype(np.float32)
    np.savez_compressed(run / "eval_data.npz", x_val=x, xp_val=x, y_val=np.arange(n) % len(keys), src_val=np.zeros(n, int),
                        neg_heldout=x, negp_heldout=x, neg_calib=x, negp_calib=x)
    return run, ckpt, model


@pytest.fixture()
def seven(tmp_path, canonical, labels, sample_rig):
    return _fake_run(tmp_path, canonical, labels.keys, labels.zh, sample_rig)


@pytest.fixture()
def five(tmp_path, canonical, sample_rig):
    ls = load_label_set("configs/label_sets/laila_5class.yaml")
    return _fake_run(tmp_path, canonical, ls.keys, ls.zh, sample_rig, label_set=ls.label_set)


def _golden(tmp_path, entries, name="g.json", **top):
    g = {"schema_version": 1, "rig": "sample_rig", "status": "占位", "entries": entries}
    g.update(top)
    p = tmp_path / name
    p.write_text(json.dumps(g, ensure_ascii=False), encoding="utf-8")  # allow_nan 默认开：NaN / Infinity 会原样写出
    return p


def _eval(path, run_ckpt_model, rig, canonical, **kw):
    _, ckpt, model = run_ckpt_model
    return evaluate_golden(path, rig, model, canonical, ckpt, **kw)


GOOD = [{"id": "a", "expect": "happy", "sliders": {"mouth_corner_L": 0.8}},
        {"id": "b", "expect": "unknown", "sliders": {"mouth_corner_L": 1.0, "mouth_corner_R": -1.0}}]


def test_rig_name_mismatch_raises_unless_allowed(tmp_path, seven, sample_rig, canonical):
    p = _golden(tmp_path, GOOD, rig="laila_v1")
    with pytest.raises(GoldenError, match="laila_v1.*sample_rig.*不符"):
        _eval(p, seven, sample_rig, canonical)
    r = _eval(p, seven, sample_rig, canonical, allow_skip=True)
    assert "skipped" in r and "--allow-golden-skip" in r["skipped"]


def test_rig_hash_must_match(tmp_path, seven, sample_rig, canonical):
    with pytest.raises(GoldenError, match="rig_hash"):
        _eval(_golden(tmp_path, GOOD, rig_hash="0" * 64), seven, sample_rig, canonical)
    r = _eval(_golden(tmp_path, GOOD, rig_hash=sample_rig.config_hash()), seven, sample_rig, canonical)
    assert r["n"] == 2


@pytest.mark.parametrize("entry, msg", [
    ({"id": "x", "expect": "happy", "sliders": {"mouth_corner_L": float("nan")}}, "不是有限数"),
    ({"id": "x", "expect": "happy", "sliders": {"mouth_corner_L": float("inf")}}, "不是有限数"),
    ({"id": "x", "expect": "happy", "sliders": {"mouth_corner_L": "0.5"}}, "不是有限数"),
    ({"id": "x", "expect": "happy", "sliders": {"mouth_corner_L": True}}, "不是有限数"),
    ({"id": "x", "expect": "happy", "sliders": {"mouth_corner_L": 1.5}}, "超出范围"),
    ({"id": "x", "expect": "happy", "sliders": {"mouth_open": -0.2}}, "超出范围"),
    ({"id": "x", "expect": "happy", "sliders": {"no_such": 0.1}}, "没有的滑杆"),
    ({"id": "x", "expect": "joy", "sliders": {}}, "不是合法标注类"),
    ({"id": "x", "expect": "surprise_fear", "sliders": {}}, "不是合法标注类"),  # 类别集的类不是标注类
    ({"id": "a", "expect": "happy", "sliders": {}}, "id 重复"),
    ({"id": "", "expect": "happy", "sliders": {}}, "id 缺失"),
    ({"id": "x", "expect": "happy", "sliders": {}, "group": ""}, "group"),
])
def test_entry_guards(tmp_path, seven, sample_rig, canonical, entry, msg):
    with pytest.raises(GoldenError, match=msg):
        _eval(_golden(tmp_path, GOOD + [entry]), seven, sample_rig, canonical)


def test_missing_file_and_schema(tmp_path, seven, sample_rig, canonical):
    with pytest.raises(GoldenError, match="不存在"):
        _eval(tmp_path / "nope.json", seven, sample_rig, canonical)
    with pytest.raises(GoldenError, match="schema_version"):
        _eval(_golden(tmp_path, GOOD, schema_version=2), seven, sample_rig, canonical)


def test_ambiguous_alias_counts_as_unknown(tmp_path, seven, sample_rig, canonical):
    r = _eval(_golden(tmp_path, GOOD + [{"id": "c", "expect": "ambiguous", "sliders": {}}]), seven, sample_rig, canonical)
    assert r["n_weird"] == 2 and r["final"]["n_unknown"] == 2
    assert next(e for e in r["entries"] if e["id"] == "c")["target"] == "unknown"


def test_sample_golden_still_passes_guards(seven, sample_rig, canonical):
    r = _eval(SAMPLE_GOLDEN, seven, sample_rig, canonical)
    assert r["n"] == 32 and r["n_known"] == 27 and r["n_weird"] == 5 and r["status_zh"] == "占位，不能证明泛化"


def test_open_set_metrics_by_hand():
    # 类 0 两个：一对一被拒；类 1 一个判对；unknown 两个：一个被拒、一个被误判成类 1
    m = open_set_metrics(np.array([0, 0, 1, -1, -1]), np.array([0, -1, 1, -1, 1]), ["a", "b"])
    assert m["confusion"] == [[1, 0, 1], [0, 1, 0], [0, 1, 1]]
    assert m["rows"] == ["a", "b", "unknown"] and m["cols"] == ["a", "b", "认不出"]
    a, b = m["per_class"]
    assert a["recall"] == 0.5 and a["precision"] == 1.0 and a["reject_rate"] == 0.5
    assert b["recall"] == 1.0 and b["precision"] == 0.5          # unknown 被误判成 b 计入精确率分母
    assert m["known_accuracy"] == pytest.approx(2 / 3)            # 被拒识计为错误
    assert m["known_reject_rate"] == pytest.approx(1 / 3)
    assert m["unknown_reject_rate"] == 0.5
    assert m["known_macro_f1"] == pytest.approx((2 / 3 + 2 / 3) / 2)


def test_label_set_golden_mapping_and_out_of_set(tmp_path, five, sample_rig, canonical):
    entries = [{"id": "s", "expect": "surprise", "sliders": {"brow_L_y": 0.9, "brow_R_y": 0.9}},
               {"id": "f", "expect": "fear", "sliders": {"eye_L_open": 1.0, "eye_R_open": 1.0}},
               {"id": "d1", "expect": "disgust", "sliders": {"nose_sneer": 0.9}},
               {"id": "d2", "expect": "disgust", "sliders": {"upper_lip": 0.8}},
               {"id": "u", "expect": "unknown", "sliders": {"mouth_x": 1.0}}]
    r = _eval(_golden(tmp_path, entries), five, sample_rig, canonical, thresholds={"tnr95": 100.0})
    assert r["n"] == 5 and r["n_known"] == 2 and r["n_weird"] == 1          # 集外类不计入
    assert r["out_of_set"]["n"] == 2 and sum(r["out_of_set"]["destinations"]["disgust"].values()) == 2
    assert r["final"]["n_known"] == 2 and r["final"]["n_unknown"] == 1
    assert [p["key"] for p in r["final"]["per_class"]] == ["neutral", "happy", "sad", "surprise_fear", "angry"]
    assert r["final"]["per_class"][3]["support"] == 2
    ents = {e["id"]: e for e in r["entries"]}
    assert ents["s"]["target"] == "surprise_fear" and ents["f"]["target"] == "surprise_fear"
    assert ents["d1"]["ok"] is None and ents["d1"]["out_of_set"] is True
    t = r["by_threshold"]["tnr95"]
    assert t["n_known"] == 2 and t["n_weird"] == 1 and t["known_pass"] == 2   # 阈值极宽：已知类都通过


def test_report_status_tag_and_skip(tmp_path, seven, canonical):
    run, _, _ = seven
    m0 = evaluate_run(run, golden=SAMPLE_GOLDEN, log=lambda *_: None)
    rep0 = (run / "report.md").read_text(encoding="utf-8")
    assert "占位，不能证明泛化" in rep0 and "最终判定口径" in rep0 and "val_final" in m0

    human = _golden(tmp_path, GOOD, name="h.json", status="human-labeled")
    before = (run / "metrics.json").read_bytes()
    m = evaluate_run(run, golden=human, log=lambda *_: None, tag="dev")
    assert (run / "metrics_dev.json").exists() and (run / "report_dev.md").exists()
    assert (run / "metrics.json").read_bytes() == before            # 带 tag 不覆盖默认产物
    rep = (run / "report_dev.md").read_text(encoding="utf-8")
    assert m["golden"]["status_zh"] == "人工盲标" and "人工盲标" in rep and "占位金标" not in rep
    assert m["eval_tag"] == "dev" and "评估标签 dev" in rep

    other = _golden(tmp_path, GOOD, name="o.json", rig="laila_v1")
    with pytest.raises(GoldenError):
        evaluate_run(run, golden=other, log=lambda *_: None, tag="x")
    assert not (run / "metrics_x.json").exists()                    # 报错时不写报告
    evaluate_run(run, golden=other, log=lambda *_: None, tag="x", allow_golden_skip=True)
    assert "已跳过" in (run / "report_x.md").read_text(encoding="utf-8")


def test_output_names():
    assert output_names(None) == ("metrics.json", "report.md")
    assert output_names("test_1") == ("metrics_test_1.json", "report_test_1.md")
    with pytest.raises(ValueError):
        output_names("../x")


def test_evaluate_cli_exit_codes(tmp_path, seven, capsys):
    from exprnet.evaluate import main as eval_main

    run, _, _ = seven
    other = _golden(tmp_path, GOOD, name="o.json", rig="laila_v1")
    assert eval_main(["--run", str(run), "--golden", str(other)]) == 2
    assert "不符" in capsys.readouterr().err
    assert eval_main(["--run", str(run), "--golden", str(other), "--allow-golden-skip", "--tag", "skip"]) == 0


def test_train_rejects_mismatched_golden_before_training(tmp_path):
    from exprnet.train import load_config, train

    cfg = load_config()
    cfg["rig"] = None  # 单位绑定 + 默认的 sample_rig 金标：训练开始前就报错，不白训
    with pytest.raises(GoldenError, match="identity"):
        train(cfg, "never", out_root=tmp_path / "runs", log=lambda *_: None, use_cache=False)
    assert not (tmp_path / "runs" / "never").exists()


# ---------------------------------------------------------------- golden_check 命令


def _split_files(tmp_path, dev_groups, test_groups):
    mk = lambda gs, pre: [{"id": f"{pre}{i}", "expect": "happy", "sliders": {"mouth_corner_L": 0.5}, "group": g}  # noqa: E731
                          for i, g in enumerate(gs)]
    d = _golden(tmp_path, mk(dev_groups, "d"), name="dev.json", status="human-labeled")
    t = _golden(tmp_path, mk(test_groups, "t"), name="test.json", status="human-labeled")
    return d, t


def test_golden_check_groups(tmp_path, capsys):
    d, t = _split_files(tmp_path, ["g1", "g2"], ["g3"])
    assert golden_check_main(["--dev", str(d), "--test", str(t)]) == 0
    assert "全部通过" in capsys.readouterr().out
    d, t = _split_files(tmp_path, ["g1", "g2"], ["g2", "g3"])
    assert golden_check_main(["--dev", str(d), "--test", str(t)]) == 1
    out = capsys.readouterr().out
    assert "group「g2」同时出现在开发集" in out and "不通过" in out


def test_golden_check_reports_each_guard(tmp_path, capsys):
    bad = copy.deepcopy(GOOD) + [{"id": "a", "expect": "joy", "sliders": {"mouth_corner_L": 2.0}}]
    d = _golden(tmp_path, bad, name="dev.json", rig_hash="deadbeef")
    assert golden_check_main(["--dev", str(d)]) == 1
    out = capsys.readouterr().out
    for s in ("rig_hash", "id 重复", "不是合法标注类", "超出范围"):
        assert s in out, s


def test_golden_check_unknown_rig_name(tmp_path, capsys):
    d = _golden(tmp_path, GOOD, name="dev.json", rig="no_such_rig_for_test")
    assert golden_check_main(["--dev", str(d)]) == 2  # configs/rigs/ 里找不到同名绑定：要求显式 --rig / --run
    assert "--rig" in capsys.readouterr().err
