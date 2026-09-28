"""FACS 合成器：形状、可复现、类间可分、出处标注、负样本。"""

import numpy as np
import pytest

from exprnet.common import CONFIG_DIR, load_yaml
from exprnet.facs import VALID_SOURCES, FacsSynthesizer, _nn_distance, parse_intensity


@pytest.fixture(scope="module")
def synth(canonical, labels):
    return FacsSynthesizer(canonical, labels.keys)


def test_shapes_and_range(synth, canonical, labels):
    X, y = synth.generate(50, 0)
    assert X.shape == (50 * labels.num_classes, canonical.dim)
    assert X.dtype == np.float32 and y.shape == (len(X),)
    assert X.min() >= 0 and X.max() <= 1
    assert np.array_equal(np.bincount(y), np.full(labels.num_classes, 50))


def test_reproducible_by_seed(synth):
    a, ya = synth.generate(30, 123)
    b, yb = synth.generate(30, 123)
    c, _ = synth.generate(30, 124)
    assert np.array_equal(a, b) and np.array_equal(ya, yb)
    assert not np.array_equal(a, c)


def test_class_means_distinct(synth, labels, canonical):
    X, y = synth.generate(300, 1)
    act = canonical.active_indices
    means = np.stack([X[y == c][:, act].mean(0) for c in range(labels.num_classes)])
    for i in range(len(means)):
        for j in range(i + 1, len(means)):
            assert np.linalg.norm(means[i] - means[j]) > 0.15, (labels.keys[i], labels.keys[j])


def test_happy_has_smile(synth, labels, canonical):
    X, y = synth.generate(200, 2)
    h = X[y == labels.index("happy")]
    s = canonical.index("mouthSmileLeft")
    assert h[:, s].mean() > 0.4
    assert X[y == labels.index("neutral")][:, s].mean() < 0.1


def test_every_variant_has_source():
    cfg = load_yaml(CONFIG_DIR / "facs.yaml")
    for key, emo in cfg["emotions"].items():
        srcs = {v["source"] for v in emo["variants"]}
        assert srcs <= VALID_SOURCES, key
        if key != "neutral":
            # 每种情绪都要同时有 EMFACS 典型原型与 CK+ 最小判据两类出处
            assert {"emfacs", "ckplus_min"} <= srcs, key


def test_ckplus_min_variants_follow_table2():
    """CK+ Table 2 的几条硬性判据：愤怒要 23+24，惊讶的 AU5 不超过 B，高兴要 12。"""
    emo = load_yaml(CONFIG_DIR / "facs.yaml")["emotions"]
    ck = {k: [v for v in e["variants"] if v["source"] == "ckplus_min"] for k, e in emo.items()}
    assert all({"AU23", "AU24"} <= set(v["aus"]) for v in ck["angry"])
    assert all("AU12" in v["aus"] for v in ck["happy"])
    for v in ck["surprise"]:
        if "AU5" in v["aus"]:
            lo, hi = parse_intensity(v["aus"]["AU5"], "B-E")
            assert hi <= 1  # A 或 B


def test_parse_intensity():
    assert parse_intensity("B", "B-E") == (1, 1)
    assert parse_intensity("B-E", "B-E") == (1, 4)
    assert parse_intensity("*", "C-D") == (2, 3)


def test_negatives(synth, canonical, sample_rig):
    ref, _ = synth.generate(100, 9)
    N = synth.negatives(400, 5, rig=sample_rig, reference=ref)
    assert N.shape == (400, canonical.dim)
    assert N.min() >= 0 and N.max() <= 1
    act = canonical.active_indices
    d = _nn_distance(N[:, act], ref[:, act])
    assert d.min() >= synth.cfg["negatives"]["min_distance_to_positive"] - 1e-5
    N2 = synth.negatives(400, 5, rig=sample_rig, reference=ref)
    assert np.array_equal(N, N2)
    N3 = synth.negatives(100, 6, rig=None, reference=ref)
    assert N3.shape == (100, canonical.dim)


def test_ckplus_fig1_variants():
    """CK+ Figure 1 示例：张嘴笑 AU6+12+25、张口惊讶 AU1+2+5+25+27，标了出处 ckplus_fig1。"""
    cfg = load_yaml(CONFIG_DIR / "facs.yaml")
    assert "ckplus_fig1" in cfg["sources"] and "Figure 1" in cfg["sources"]["ckplus_fig1"]
    emo = cfg["emotions"]
    fig1 = {k: [v for v in e["variants"] if v["source"] == "ckplus_fig1"] for k, e in emo.items()}
    assert [set(v["aus"]) for v in fig1["happy"]] == [{"AU6", "AU12", "AU25"}]
    assert [set(v["aus"]) for v in fig1["surprise"]] == [{"AU1", "AU2", "AU5", "AU25", "AU27"}]
    assert all(not v for k, v in fig1.items() if k not in ("happy", "surprise"))
    for v in fig1["happy"] + fig1["surprise"]:
        assert not v.get("choose") and not v.get("optional")  # 只有 Figure 1 里的 AU，没有额外添加


def _class_support(synth, key):
    """某类所有变体可能激活的表情基（核心 AU、choose、optional 的并集）。"""
    sup = np.zeros(synth.D, dtype=bool)
    for v in synth.variants[key]:
        aus = list(v.aus) + [a for c in v.choose for a in c] + list(v.optional)
        for au in aus:
            sup |= synth.au_vec[au] > 0
    return sup


def test_clean_samples_ratio_and_no_noise(synth, canonical, labels):
    n = 40
    X, y, clean = synth.generate(n, 11, return_clean=True)
    frac = synth.clean_fraction
    assert frac == pytest.approx(0.25)
    for c in range(labels.num_classes):
        assert clean[y == c].sum() == round(n * frac)  # 每类按比例取整
    masked = canonical.masked
    for c, key in enumerate(labels.keys):
        sup = _class_support(synth, key)
        xc = X[(y == c) & clean]
        assert np.all(xc[:, ~sup] == 0), key            # 没有干扰 AU、没有噪声：核心 AU 之外全是 0
        assert np.all(xc[:, masked & ~sup] == 0), key   # 没有随机眼球朝向
        assert np.array_equal(xc, canonical.mirror(xc)), key  # 没有左右抖动（这 7 类都没有单侧 AU）
        noisy = X[(y == c) & ~clean]
        assert (noisy[:, ~sup] > 0).any(), key          # 对照：非干净样本带噪声
    assert np.all(X[(y == labels.index("neutral")) & clean] == 0)


def test_clean_fraction_configurable(canonical, labels):
    cfg = load_yaml(CONFIG_DIR / "facs.yaml")
    cfg["synthesis"]["clean_fraction"] = 0.5
    s2 = FacsSynthesizer(canonical, labels.keys, cfg=cfg)
    _, y, clean = s2.generate(30, 0, return_clean=True)
    assert clean.sum() == 15 * labels.num_classes
    cfg["synthesis"]["clean_fraction"] = 0.0
    _, _, clean0 = FacsSynthesizer(canonical, labels.keys, cfg=cfg).generate(10, 0, return_clean=True)
    assert not clean0.any()
