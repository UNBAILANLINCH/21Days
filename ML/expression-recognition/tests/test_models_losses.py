"""模型前向、参数量；损失；增强；校准与评估指标。"""

import math

import numpy as np
import pytest
import torch

from exprnet.augment import mirror, mixup
from exprnet.calibrate import (energy_np, fit_temperature, pick_threshold, softmax_np, threshold_id_pass,
                               threshold_ood_reject)
from exprnet.evaluate import auroc, classification_metrics, ece_score, fpr_at_tpr
from exprnet.losses import ExprLoss, outlier_exposure_loss, soft_cross_entropy
from exprnet.models import build_model, count_params

MODEL_CFG = {
    "regionformer": {"d_model": 48, "heads": 4, "layers": 2, "ffn_mult": 2, "dropout": 0.1},
    "resmlp": {"width": 128, "hidden": 256, "blocks": 2, "dropout": 0.1},
}


@pytest.mark.parametrize("name", ["regionformer", "resmlp"])
def test_forward_shape_and_params(name, canonical):
    m = build_model(name, MODEL_CFG, canonical, 7).eval()
    x = torch.rand(5, canonical.dim)
    assert m(x).shape == (5, 7)
    assert m(x[:1]).shape == (1, 7)
    assert count_params(m) < 200_000


def test_regionformer_ignores_masked_dims(canonical):
    m = build_model("regionformer", MODEL_CFG, canonical, 7).eval()
    x = torch.rand(4, canonical.dim)
    x2 = x.clone()
    x2[:, torch.from_numpy(canonical.masked)] = 1.0
    assert torch.allclose(m(x), m(x2))


def test_oe_loss_minimal_at_uniform():
    C = 7
    uniform = torch.zeros(3, C, requires_grad=True)
    loss_u = outlier_exposure_loss(uniform)
    assert loss_u.item() == pytest.approx(math.log(C), abs=1e-6)
    loss_u.backward()
    assert torch.allclose(uniform.grad, torch.zeros_like(uniform.grad), atol=1e-7)
    for seed in range(5):
        g = torch.Generator().manual_seed(seed)
        z = torch.randn(3, C, generator=g) * 3
        assert outlier_exposure_loss(z).item() > loss_u.item()


def test_soft_ce_with_smoothing_and_adjustment():
    z = torch.tensor([[2.0, 0.0, -1.0]])
    t = torch.tensor([[1.0, 0.0, 0.0]])
    eps = 0.1
    ts = t * (1 - eps) + eps / 3
    manual = -(ts * torch.log_softmax(z, -1)).sum()
    assert soft_cross_entropy(z, t, eps).item() == pytest.approx(manual.item(), abs=1e-6)
    prior = torch.log(torch.tensor([0.8, 0.1, 0.1]))
    adj = soft_cross_entropy(z, t, 0.0, prior, 1.0)
    manual2 = -torch.log_softmax(z + prior, -1)[0, 0]
    assert adj.item() == pytest.approx(manual2.item(), abs=1e-6)


def test_expr_loss_parts():
    lf = ExprLoss({"label_smoothing": 0.1, "logit_adjust_tau": 1.0, "oe_weight": 0.5, "sce": {"enabled": True}},
                  torch.full((7,), 1 / 7))
    loss, parts = lf(torch.randn(4, 7), torch.eye(7)[:4], torch.randn(3, 7))
    assert torch.isfinite(loss) and "ce" in parts and "oe" in parts


def test_mirror_and_mixup(canonical):
    g = torch.Generator().manual_seed(0)
    x = torch.rand(6, canonical.dim)
    mi = torch.from_numpy(canonical.mirror_index)
    assert torch.equal(mirror(mirror(x, mi, 1.0, g), mi, 1.0, g), x)
    y = torch.eye(7)[:6]
    xm, ym = mixup(x, y, 0.4, g)
    assert xm.shape == x.shape
    assert torch.allclose(ym.sum(-1), torch.ones(6))


def test_temperature_recovers_scale():
    rng = np.random.default_rng(0)
    true_logits = rng.normal(0, 2, size=(4000, 5))
    y = np.array([rng.choice(5, p=p) for p in softmax_np(true_logits)])
    T = fit_temperature(true_logits * 3.0, y)  # 过度自信 3 倍 → T 应接近 3
    assert 2.5 < T < 3.5


def test_energy_matches_torch():
    z = np.random.default_rng(1).normal(size=(10, 7)) * 5
    T = 1.7
    ref = (-T * torch.logsumexp(torch.from_numpy(z) / T, -1)).numpy()
    assert np.allclose(energy_np(z, T), ref, atol=1e-10)
    assert threshold_id_pass(np.arange(101.0), 0.95) == pytest.approx(95.0)


def test_metrics():
    y = np.array([0, 0, 1, 1, 2, 2])
    p = np.array([0, 1, 1, 1, 2, 0])
    m = classification_metrics(y, p, ["a", "b", "c"])
    assert m["accuracy"] == pytest.approx(4 / 6)
    assert np.array(m["confusion"]).sum() == 6
    assert auroc(np.zeros(10), np.ones(10)) == 1.0
    assert auroc(np.ones(10), np.zeros(10)) == 0.0
    assert fpr_at_tpr(np.linspace(0, 1, 100), np.full(50, 2.0)) == 0.0
    probs = np.eye(3)[y]
    assert ece_score(probs, y) == pytest.approx(0.0)


def test_threshold_tnr95_on_constructed_data():
    """TNR95：阈值取负样本 energy 的第 5 百分位，95% 的负样本 energy 高于它（被拒）。"""
    rng = np.random.default_rng(0)
    e_ood = rng.permutation(np.arange(101.0))  # 0..100，第 5 百分位正好是 5
    e_id = rng.normal(-5.0, 0.1, size=1000)
    assert threshold_ood_reject(e_ood, 0.95) == pytest.approx(5.0)
    thr, both = pick_threshold("tnr95", e_id, e_ood)
    assert thr == pytest.approx(5.0) and both["tnr95"] == pytest.approx(5.0)
    assert (e_ood > thr).mean() == pytest.approx(0.95, abs=0.01)
    assert (e_id <= thr).mean() == 1.0
    # 旧规则只作对照：取分布内第 95 百分位
    assert both["id95"] == pytest.approx(np.quantile(e_id, 0.95))
    thr_old, _ = pick_threshold("id95", e_id, e_ood)
    assert thr_old == pytest.approx(both["id95"])
    with pytest.raises(ValueError):
        pick_threshold("nope", e_id, e_ood)
