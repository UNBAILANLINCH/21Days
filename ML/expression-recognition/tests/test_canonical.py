"""规范空间：维数、命名口径、左右配对自洽。"""

import numpy as np

from exprnet.canonical import load_canonical
from exprnet.extract import MEDIAPIPE_BLENDSHAPE_NAMES


def test_dim_and_names(canonical):
    assert canonical.dim == 51
    assert len(set(canonical.names)) == 51
    assert "_neutral" not in canonical.names and "tongueOut" not in canonical.names


def test_matches_mediapipe_by_name(canonical):
    # MediaPipe Tasks API 52 项 = _neutral + 51 个表情基
    assert len(MEDIAPIPE_BLENDSHAPE_NAMES) == 52
    assert MEDIAPIPE_BLENDSHAPE_NAMES[0] == "_neutral"
    assert set(MEDIAPIPE_BLENDSHAPE_NAMES) - {"_neutral"} == set(canonical.names)


def test_mirror_is_involution_and_pairs_consistent(canonical):
    mi = canonical.mirror_index
    assert np.array_equal(mi[mi], np.arange(canonical.dim))
    for i, n in enumerate(canonical.names):
        j = int(mi[i])
        if canonical.sides[i] == "C":
            assert j == i
        else:
            assert j != i
            assert {canonical.sides[i], canonical.sides[j]} == {"L", "R"}
            assert canonical.regions[i] == canonical.regions[j]
            m = canonical.names[j]
            assert n.replace("Left", "#").replace("Right", "Left").replace("#", "Right") == m


def test_mirror_swaps_values(canonical):
    x = canonical.vector({"mouthSmileLeft": 0.8, "browInnerUp": 0.3})
    y = canonical.mirror(x)
    assert y[canonical.index("mouthSmileRight")] == np.float32(0.8)
    assert y[canonical.index("mouthSmileLeft")] == 0
    assert y[canonical.index("browInnerUp")] == np.float32(0.3)


def test_default_mask_is_eyelook(canonical):
    assert sorted(canonical.masked_names) == sorted(n for n in canonical.names if n.startswith("eyeLook"))
    assert canonical.masked.sum() == 8
    assert len(canonical.active_indices) == 43


def test_pair_groups_share(canonical):
    g = canonical.pair_groups
    for i in range(canonical.dim):
        assert g[i] == g[canonical.mirror_index[i]]
    n_center = sum(s == "C" for s in canonical.sides)
    assert len(set(g.tolist())) == n_center + (canonical.dim - n_center) // 2


def test_custom_mask():
    c = load_canonical(masked=[])
    assert c.masked.sum() == 0
