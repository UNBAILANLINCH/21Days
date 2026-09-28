"""可选：真实 MediaPipe 冒烟。只有本机装了 mediapipe、.cache/ 里已有 face_landmarker.task、
且 matplotlib 自带的示例人像存在时才跑，否则跳过（测试本身不联网、不需要数据集）。"""

from pathlib import Path

import numpy as np
import pytest

from exprnet.common import CACHE_DIR

mp = pytest.importorskip("mediapipe")
cv2 = pytest.importorskip("cv2")
matplotlib = pytest.importorskip("matplotlib")

MODEL = CACHE_DIR / "face_landmarker.task"
SAMPLE = Path(matplotlib.get_data_path()) / "sample_data" / "grace_hopper.jpg"


@pytest.mark.skipif(not MODEL.exists() or not SAMPLE.exists(), reason="缺模型文件或示例人像")
def test_real_mediapipe_on_sample_portrait(canonical):
    from exprnet.datasets import Item
    from exprnet.extract import MediaPipeExtractor, extract_items

    ext = MediaPipeExtractor(MODEL)
    try:
        items = [Item(label="neutral", path=SAMPLE, ref=SAMPLE.name),
                 Item(label="neutral", image=np.zeros((64, 64), np.uint8), ref="blank")]
        res = extract_items(items, ext, canonical, upscale=1.0, log=lambda *_: None)
    finally:
        ext.close()
    assert res["stats"]["n_detected"] == 1  # 人像检出、纯黑图检不出
    x = res["x"][0]
    assert x.shape == (51,) and 0 <= x.min() and x.max() <= 1
