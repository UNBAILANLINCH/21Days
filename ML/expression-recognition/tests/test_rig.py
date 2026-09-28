"""绑定：全零滑杆、正负半轴拆分、投影残差、哈希。"""

import copy

import numpy as np
import pytest

from exprnet.rig import identity_rig, rig_from_dict


def test_zero_sliders_give_zero(sample_rig, canonical):
    x = sample_rig.forward(np.zeros((3, sample_rig.num_sliders)))
    assert x.shape == (3, canonical.dim)
    assert np.all(x == 0)
    xi = identity_rig(canonical).forward(np.zeros((1, canonical.dim)))
    assert np.all(xi == 0)


def test_positive_negative_split(sample_rig, canonical):
    k = sample_rig.keys.index("mouth_corner_L")
    s = np.zeros((4, sample_rig.num_sliders))
    s[0, k] = 1.0
    s[1, k] = -1.0
    s[2, k] = 0.5
    s[3, k] = 3.0  # 超出范围应裁剪到 1
    x = sample_rig.forward(s)
    smile, frown = canonical.index("mouthSmileLeft"), canonical.index("mouthFrownLeft")
    assert x[0, smile] == pytest.approx(1.0) and x[0, frown] == 0
    assert x[1, frown] == pytest.approx(1.0) and x[1, smile] == 0
    assert x[2, smile] == pytest.approx(0.5)
    assert np.allclose(x[3], x[0])
    assert np.count_nonzero(x[0]) == 1 and np.count_nonzero(x[1]) == 1


def test_unipolar_slider_ignores_negative(sample_rig, canonical):
    k = sample_rig.keys.index("mouth_open")  # 范围 [0,1]
    s = np.zeros((1, sample_rig.num_sliders))
    s[0, k] = -1.0
    assert np.all(sample_rig.forward(s) == 0)


def test_projection_of_rig_generated_vectors(sample_rig, canonical):
    rng = np.random.default_rng(0)
    s = sample_rig.random_sliders(200, rng)
    mask = canonical.mask_vector
    lin = np.maximum(s, 0) @ sample_rig.W_pos + np.maximum(-s, 0) @ sample_rig.W_neg
    ok = (lin * mask).max(1) <= 1.0  # 只看没有触到 [0,1] 裁剪的样本（线性可达）
    assert ok.sum() > 100
    x = sample_rig.forward(s[ok])
    S, x_hat = sample_rig.project(x, mask)
    assert S.shape == (ok.sum(), sample_rig.num_sliders)
    assert np.abs(x * mask - x_hat).max() < 1e-4
    assert np.all(S >= sample_rig.s_min - 1e-6) and np.all(S <= sample_rig.s_max + 1e-6)


def test_projection_residual_for_unreachable(sample_rig, canonical):
    x = canonical.vector({"cheekPuff": 0.9})[None]
    _, x_hat = sample_rig.project(x, canonical.mask_vector)
    assert np.allclose(x_hat, 0)  # 示例绑定没有鼓腮滑杆


def test_identity_projection(canonical):
    r = identity_rig(canonical)
    x = np.random.default_rng(1).random((5, canonical.dim)).astype(np.float32)
    _, x_hat = r.project(x, canonical.mask_vector)
    assert np.allclose(x_hat, x * canonical.mask_vector)


def test_hash_changes_with_config(sample_rig, canonical):
    cfg = sample_rig.to_config()
    same = rig_from_dict(copy.deepcopy(cfg), canonical)
    assert same.config_hash() == sample_rig.config_hash()
    changed = copy.deepcopy(cfg)
    changed["sliders"][0]["pos"]["browOuterUpLeft"] = 0.9
    assert rig_from_dict(changed, canonical).config_hash() != sample_rig.config_hash()
    renamed = copy.deepcopy(cfg)
    renamed["sliders"][0]["zh"] = "改名"
    assert rig_from_dict(renamed, canonical).config_hash() != sample_rig.config_hash()


def test_validation_errors(canonical):
    with pytest.raises(ValueError):
        rig_from_dict({"name": "bad", "sliders": [{"key": "a", "range": [0.2, 1], "pos": {"jawOpen": 1}}]}, canonical)
    with pytest.raises(KeyError):
        rig_from_dict({"name": "bad", "sliders": [{"key": "a", "range": [0, 1], "pos": {"noSuchShape": 1}}]}, canonical)
    with pytest.raises(ValueError):
        rig_from_dict({"name": "bad", "sliders": [{"key": "a", "range": [0, 1], "neg": {"jawOpen": 1}}]}, canonical)


def test_sample_rig_shape(sample_rig):
    assert 18 <= sample_rig.num_sliders <= 22
    bipolar = sum(1 for s in sample_rig.sliders if s.min < 0 < s.max)
    assert bipolar >= 16
    assert all(s.default == 0 for s in sample_rig.sliders)
