"""阈值标定集与留出集分离：种子不同、样本不重叠、TNR95 阈值只用标定集。"""

import numpy as np
import pytest
import torch

import exprnet.train as train_mod
from exprnet.calibrate import energy_np
from exprnet.datasets import build_train_data, negative_seeds
from exprnet.evaluate import logits_of
from exprnet.facs import FacsSynthesizer
from exprnet.models import load_checkpoint
from exprnet.train import load_config, train


def test_negative_seeds_distinct():
    s = negative_seeds(42, {})
    assert len(set(s.values())) == 3
    assert negative_seeds(42, {"seed_offsets": {"calib": 999}})["calib"] == 42 + 999
    with pytest.raises(ValueError):
        negative_seeds(42, {"seed_offsets": {"calib": 202, "heldout": 202}})


def _small_cfg():
    cfg = load_config()
    cfg["data"]["sources"] = [{"kind": "synthetic", "n_per_class": 30, "seed": 5}]
    cfg["data"]["negatives"] = {"train": 120, "heldout": 90, "calib": None}
    return cfg


def test_calib_and_heldout_disjoint(canonical, labels, sample_rig):
    cfg = _small_cfg()
    synth = FacsSynthesizer(canonical, labels.keys)
    data = build_train_data(cfg, canonical, labels, synth, sample_rig, log=lambda *_: None, use_cache=False)
    info = data.negatives_info
    assert info["calib"]["seed"] != info["heldout"]["seed"] != info["train"]["seed"]
    assert info["calib"]["seed"] != info["train"]["seed"]
    assert len(data.neg_calib) == len(data.neg_heldout) == 90  # calib: null → 与留出集同数量
    rows = lambda a: {r.tobytes() for r in np.ascontiguousarray(a, dtype=np.float32)}
    assert not rows(data.neg_calib) & rows(data.neg_heldout)
    assert not rows(data.neg_calib) & rows(data.neg_train)
    assert not rows(data.neg_heldout) & rows(data.neg_train)


def test_threshold_uses_only_calib(tmp_path, monkeypatch):
    seen = {}
    real = train_mod.pick_threshold

    def spy(rule, e_id, e_ood, *a, **k):
        seen["e_ood"] = np.array(e_ood)
        return real(rule, e_id, e_ood, *a, **k)

    monkeypatch.setattr(train_mod, "pick_threshold", spy)
    cfg = _small_cfg()
    cfg["model"]["name"] = "resmlp"
    cfg["optim"]["epochs"] = 1
    cfg["optim"]["batch_size"] = 64
    cfg["golden"] = None
    run = train(cfg, "calib_only", out_root=tmp_path / "runs", log=lambda *_: None, use_cache=False)

    clf, ckpt, canonical = load_checkpoint(run)
    ev = np.load(run / "eval_data.npz")
    T = ckpt["temperature"]
    with torch.no_grad():
        e_cal = energy_np(logits_of(clf, ev["negp_calib"], canonical.mask_vector), T)
        e_held = energy_np(logits_of(clf, ev["negp_heldout"], canonical.mask_vector), T)
    # 阈值函数拿到的负样本 energy 就是标定集的，不是留出集的
    assert len(seen["e_ood"]) == len(ev["negp_calib"])
    assert np.allclose(seen["e_ood"], e_cal, atol=1e-5)
    assert not np.allclose(np.sort(seen["e_ood"]), np.sort(e_held))
    assert ckpt["energy_threshold"] == pytest.approx(np.quantile(e_cal, 0.05), abs=1e-5)
    assert ckpt["negatives"]["calib"]["seed"] != ckpt["negatives"]["heldout"]["seed"]
