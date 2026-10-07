"""候选17轴工程检查；不宣称几何/人工语义校准已完成。"""
import numpy as np
from exprnet.common import CONFIG_DIR
from exprnet.rig import load_rig


def test_brow_segments_preserve_distinct_positive_information(canonical):
    rig = load_rig(CONFIG_DIR / "rigs/laila_rig_v2_candidate.yaml", canonical)
    assert rig.name == "laila_v2_candidate" and rig.num_sliders == 17
    assert len(set(rig.keys)) == 17
    x = rig.forward(np.eye(17, dtype=np.float32))
    assert not np.allclose(x[0], x[1])
    assert not np.allclose(x[1], x[2])
    # 同侧内眉上抬+外眉下压可共存，不再被整眉双极轴抵消。
    s = np.zeros((1, 17)); s[0, 0] = 1; s[0, 2] = -1
    y = rig.forward(s)[0]
    assert y[canonical.index("browInnerUp")] > 0
    assert y[canonical.index("browDownRight")] > 0


def test_shared_brow_and_separate_lips(canonical):
    rig = load_rig(CONFIG_DIR / "rigs/laila_rig_v2_candidate.yaml", canonical)
    s = np.zeros((3, 17)); s[0, :6] = .5; s[1, :6] = 1; s[2, 14] = 1
    x = rig.forward(s)
    assert np.isclose(x[0, canonical.index("browInnerUp")], .5)
    assert np.isclose(x[1, canonical.index("browInnerUp")], 1)
    assert x[2, canonical.index("mouthUpperUpRight")] == 1
    assert x[2, canonical.index("mouthUpperUpLeft")] == 0
    assert not rig.reachable()[canonical.index("noseSneerLeft")]
    assert not rig.reachable()[canonical.index("jawOpen")]
