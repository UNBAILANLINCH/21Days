"""类别集（label set）与按绑定剔除做不出来的变体（规格 §15.1、§15.2）。"""

import copy

import numpy as np
import pytest

from exprnet.common import PROJECT_ROOT, apply_label_set, load_label_set, load_labels
from exprnet.facs import FacsSynthesizer, plan_variant_filter
from exprnet.rig import identity_rig, load_rig

LAILA_5 = "configs/label_sets/laila_5class.yaml"
OPTIMISTIC = PROJECT_ROOT / "analysis" / "laila_upper_bound" / "laila_candidate_optimistic.yaml"


@pytest.fixture(scope="module")
def ls5():
    return load_label_set(LAILA_5)


@pytest.fixture(scope="module")
def optimistic(canonical):
    return load_rig(OPTIMISTIC, canonical)


# ---------------------------------------------------------------- 类别集


def test_label_set_loads(ls5, labels):
    assert ls5.keys == ["neutral", "happy", "sad", "surprise_fear", "angry"]
    assert ls5.zh[3] == "惊讶/恐惧"
    assert ls5.annotation_keys == labels.keys  # 标注类仍是 labels.yaml 的七类
    assert ls5.dropped == ["disgust"]
    assert ls5.members()["surprise_fear"] == ["surprise", "fear"]


def test_label_set_maps_annotations(ls5):
    sf = ls5.index("surprise_fear")
    assert ls5.to_class_index("fear") == sf and ls5.to_class_index("Surprised") == sf  # 别名先归一再映射
    assert ls5.to_class_index("surprise_fear") == sf
    assert ls5.to_class_index("disgust") is None      # 集外类：丢弃
    assert ls5.to_class_index("contempt") is None     # 未启用的标注类
    assert ls5.map_annotation("fear") == "surprise_fear"
    assert ls5.map_annotation("disgust") is None
    assert ls5.map_annotation("unknown") == "unknown"


def test_default_labels_unchanged(labels):
    assert labels.label_set is None and labels.class_map == {} and labels.dropped == []
    assert labels.members() == {k: [k] for k in labels.keys}
    assert labels.to_class_index("fear") == labels.index("fear")
    assert labels.to_class_index("contempt") is None
    assert labels.annotation_keys == labels.keys


def _cfg():
    return {"name": "t", "classes": [{"key": "neutral", "zh": "中性", "from": ["neutral"]},
                                     {"key": "happy", "zh": "高兴", "from": ["happy"]},
                                     {"key": "sad", "zh": "悲伤", "from": ["sad"]},
                                     {"key": "surprise_fear", "zh": "惊讶/恐惧", "from": ["surprise", "fear"]},
                                     {"key": "angry", "zh": "愤怒", "from": ["angry"]}],
            "drop": ["disgust"]}


def _bad(mut):
    c = _cfg()
    mut(c)
    return c


@pytest.mark.parametrize("cfg, msg", [
    (_bad(lambda c: c.pop("name")), "name"),
    (_bad(lambda c: c["classes"][0].pop("from")), "from"),
    (_bad(lambda c: c["classes"][0].update({"from": ["joy"]})), "joy"),
    (_bad(lambda c: c["classes"][1].update({"from": ["happy", "fear"]})), "同时出现"),
    (_bad(lambda c: c.update({"drop": []})), "disgust"),                       # 漏了标注类
    (_bad(lambda c: c.update({"drop": ["disgust", "fear"]})), "同时出现"),
    (_bad(lambda c: c["classes"][0].update({"key": "unknown"})), "unknown"),
    (_bad(lambda c: c["classes"][1].update({"key": "neutral"})), "重复"),
    (_bad(lambda c: c["classes"][3].update({"key": "fear", "from": ["surprise"]}) or c["drop"].append("fear")), "同名"),
])
def test_label_set_validation(labels, cfg, msg):
    with pytest.raises(ValueError, match=msg):
        apply_label_set(labels, cfg)


# ---------------------------------------------------------------- 合成器


def test_default_synth_identical(canonical, labels):
    """不传类别集 / 剔除清单时，合成结果与原先逐位相同（默认行为不变）。"""
    a = FacsSynthesizer(canonical, labels.keys).generate(40, 5, return_clean=True)
    b = FacsSynthesizer(canonical, labels.keys, class_members=labels.members(), exclude=[]).generate(40, 5, return_clean=True)
    for x, y in zip(a, b):
        assert np.array_equal(x, y)


@pytest.mark.parametrize("n, want", [(2000, [1000, 1000]), (7, [4, 3])])
def test_merged_class_half_half(canonical, ls5, n, want, monkeypatch):
    synth = FacsSynthesizer(canonical, ls5.keys, class_members=ls5.members())
    calls = []
    orig = synth._sample_emotion
    monkeypatch.setattr(synth, "_sample_emotion", lambda k, m, rng: calls.append((k, m)) or orig(k, m, rng))
    X, clean = synth.sample_class("surprise_fear", n, np.random.default_rng(0))
    assert calls == [("surprise", want[0]), ("fear", want[1])]
    assert X.shape == (n, canonical.dim) and clean.shape == (n,)


def test_label_set_synth_counts_and_no_dropped_class(canonical, ls5):
    synth = FacsSynthesizer(canonical, ls5.keys, class_members=ls5.members())
    assert "disgust" not in synth.variants  # 集外类不进正样本
    X, y = synth.generate(30, 1)
    assert np.array_equal(np.bincount(y), np.full(5, 30))  # 合并类总数与其他类相同


# ---------------------------------------------------------------- 按绑定剔除变体


def _dropped(info):
    return {(r["emotion"], r["variant"], r["choose"]) for r in info["dropped"]}


def test_identity_rig_drops_nothing(canonical, labels):
    synth = FacsSynthesizer(canonical, labels.keys)
    info = plan_variant_filter(synth, identity_rig(canonical), 0.9)
    assert info["dropped"] == [] and "单位绑定" in info["note"]
    assert max(r["rel_residual"] for r in info["variants"] if r["rel_residual"] is not None) < 1e-9


def test_sample_rig_drops_nothing(canonical, labels, sample_rig):
    assert plan_variant_filter(FacsSynthesizer(canonical, labels.keys), sample_rig, 0.9)["dropped"] == []


def test_optimistic_rig_drops_per_alternative(canonical, labels, optimistic):
    """乐观候选：愤怒的纯 AU23+24、厌恶的纯 AU9 做不出来；厌恶「9+17 / 10+17」只剔除 AU9 那个备选，保留 AU10+17。"""
    info = plan_variant_filter(FacsSynthesizer(canonical, labels.keys), optimistic, 0.9)
    assert _dropped(info) == {("angry", 2, None), ("disgust", 1, 0), ("disgust", 2, None)}
    kept = next(r for r in info["variants"] if (r["emotion"], r["variant"], r["choose"]) == ("disgust", 1, 1))
    assert set(kept["aus"]) == {"AU10", "AU17"} and kept["rel_residual"] < 0.9
    assert all(r["source"] and r["class"] for r in info["dropped"])

    synth = FacsSynthesizer(canonical, labels.keys, exclude=info["dropped"])
    dis = synth.variants["disgust"]
    assert len(dis) == 3 and len(synth.variants["angry"]) == 2
    v1 = next(v for v in dis if v.choose)
    assert v1.choose == [{"AU10": "*"}] and v1.weight == pytest.approx(0.5)  # 备选保留一半，权重按比例缩小
    X, y = synth.generate(20, 0)
    assert np.array_equal(np.bincount(y), np.full(labels.num_classes, 20))


def test_label_set_filter_only_checks_members(canonical, ls5, optimistic):
    info = plan_variant_filter(FacsSynthesizer(canonical, ls5.keys, class_members=ls5.members()), optimistic, 0.9)
    assert {r["emotion"] for r in info["variants"]} == {"neutral", "happy", "sad", "surprise", "fear", "angry"}
    assert _dropped(info) == {("angry", 2, None)}
    assert {r["class"] for r in info["variants"] if r["emotion"] in ("surprise", "fear")} == {"surprise_fear"}


def test_excluding_everything_raises(canonical, labels):
    ex = [{"emotion": "angry", "variant": i, "choose": None} for i in range(3)]
    with pytest.raises(ValueError, match="全部被剔除"):
        FacsSynthesizer(canonical, labels.keys, exclude=ex)


def test_bad_threshold(canonical, labels, sample_rig):
    with pytest.raises(ValueError):
        plan_variant_filter(FacsSynthesizer(canonical, labels.keys), sample_rig, 0.0)


def test_train_cli_flags_reach_config(monkeypatch):
    import exprnet.train as tr

    seen = {}
    monkeypatch.setattr(tr, "train", lambda cfg, name, use_cache=True: seen.update(cfg=copy.deepcopy(cfg)))
    assert tr.main(["--name", "x", "--label-set", "laila_5class", "--drop-infeasible-variants",
                    "--infeasible-threshold", "0.8", "--allow-golden-skip"]) == 0
    cfg = seen["cfg"]
    assert cfg["label_set"] == "configs/label_sets/laila_5class.yaml"
    assert cfg["infeasible_variants"]["drop"] is True and cfg["infeasible_variants"]["rel_residual_threshold"] == 0.8
    assert cfg["golden_allow_skip"] is True
    seen.clear()
    assert tr.main(["--name", "x"]) == 0  # 不传新参数：配置里是默认值
    cfg = seen["cfg"]
    assert cfg["label_set"] is None and cfg["infeasible_variants"]["drop"] is False and cfg["golden_allow_skip"] is False
