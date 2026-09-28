"""数据读取、特征缓存、基线归一化、划分；抽取模块用 mock 覆盖（不需要 mediapipe 与数据集）。"""

import numpy as np
import pytest

from exprnet.datasets import (Item, apply_baseline, iter_fer2013, iter_kdef, iter_rafdb, load_features,
                              neutral_baseline, save_features, split_train_val)
from exprnet.extract import MEDIAPIPE_BLENDSHAPE_NAMES, categories_to_vector, extract_items


# ---------------------------------------------------------------- 标签表


def test_dataset_label_orders(labels):
    assert labels.dataset_label("fer2013", 0) == "angry"
    assert labels.dataset_label("fer2013", 6) == "neutral"
    assert labels.dataset_label("rafdb", 1) == "surprise"
    assert labels.dataset_label("rafdb", 4) == "happy"
    assert labels.dataset_label("rafdb", 7) == "neutral"
    assert labels.dataset_label("affectnet", 0) == "neutral"
    assert labels.dataset_label("affectnet", 7) == "contempt"
    assert labels.dataset_label("affectnet", 10) == "_no_face"
    assert labels.dataset_label("ckplus", 2) == "contempt"
    assert labels.dataset_label("ckplus", 6) == "sad"
    assert labels.to_class_index("contempt") is None  # 默认未启用
    assert labels.to_class_index("Happiness") == labels.index("happy")
    assert labels.to_class_index("AN") == labels.index("angry")


# ---------------------------------------------------------------- 按名字对齐 MediaPipe 输出


class _Cat:
    def __init__(self, name, score):
        self.category_name = name
        self.score = score


def test_mediapipe_output_aligned_by_name(canonical):
    rng = np.random.default_rng(0)
    scores = {n: float(rng.random()) for n in MEDIAPIPE_BLENDSHAPE_NAMES}
    cats = [_Cat(n, scores[n]) for n in MEDIAPIPE_BLENDSHAPE_NAMES]  # 52 项，第一项 _neutral
    rng.shuffle(cats)  # 打乱顺序：必须按名字而不是下标对齐
    v = categories_to_vector(cats, canonical)
    assert v.shape == (51,)
    for i, n in enumerate(canonical.names):
        assert v[i] == pytest.approx(scores[n])


def test_arkit_style_output_also_works(canonical):
    arkit = {n: 0.5 for n in canonical.names}
    arkit["tongueOut"] = 0.9  # ARKit 口径：含 tongueOut、无 _neutral
    v = categories_to_vector(arkit, canonical)
    assert np.allclose(v, 0.5)


def test_missing_name_raises(canonical):
    cats = [(n, 0.1) for n in MEDIAPIPE_BLENDSHAPE_NAMES if n != "jawOpen"]
    with pytest.raises(ValueError):
        categories_to_vector(cats, canonical)


def test_extract_items_with_fake_extractor(canonical):
    items = [Item(label="happy", image=np.zeros((48, 48), np.uint8), ref=f"r{i}") for i in range(6)]
    items += [Item(label="sad", image=np.full((48, 48), 255, np.uint8), ref="dark"), Item(label=None, image=np.zeros((4, 4), np.uint8))]
    calls = []

    def fake_extractor(rgb):
        calls.append(rgb.shape)
        if rgb.mean() > 200:  # 模拟「没检出人脸」
            return None
        return [(n, 0.2) for n in MEDIAPIPE_BLENDSHAPE_NAMES]

    def fake_pre(img, upscale):
        return np.repeat(np.repeat(img, int(upscale), 0), int(upscale), 1)[..., None].repeat(3, -1)

    res = extract_items(items, fake_extractor, canonical, upscale=4, preprocess=fake_pre, log=lambda *_: None)
    assert res["x"].shape == (6, 51)
    st = res["stats"]
    assert st["n_total"] == 7 and st["n_detected"] == 6 and st["n_unlabeled_skipped"] == 1
    assert st["per_label"]["sad"]["rate"] == 0.0 and st["per_label"]["happy"]["rate"] == 1.0
    assert calls[0] == (192, 192, 3)


def test_to_rgb_upscale():
    cv2 = pytest.importorskip("cv2")  # noqa: F841
    from exprnet.extract import to_rgb

    out = to_rgb(np.zeros((48, 48), np.uint8), 4)
    assert out.shape == (192, 192, 3) and out.dtype == np.uint8


# ---------------------------------------------------------------- 读取器


def test_fer2013_reader(tmp_path, labels):
    p = tmp_path / "fer2013.csv"
    px = " ".join(["128"] * (48 * 48))
    p.write_text(f"emotion,pixels,Usage\n3,{px},Training\n6,{px},PublicTest\n1,{px},PrivateTest\n", encoding="utf-8")
    items = list(iter_fer2013(p, labels))
    assert [i.label for i in items] == ["happy", "neutral", "disgust"]
    assert [i.split for i in items] == ["train", "val", "test"]
    assert items[0].image.shape == (48, 48)


def test_rafdb_reader(tmp_path, labels):
    (tmp_path / "EmoLabel").mkdir()
    (tmp_path / "EmoLabel" / "list_patition_label.txt").write_text("train_00001.jpg 4\ntest_0001.jpg 1\n", encoding="utf-8")
    (tmp_path / "Image" / "aligned").mkdir(parents=True)
    items = list(iter_rafdb(tmp_path, labels))
    assert [i.label for i in items] == ["happy", "surprise"]
    assert [i.split for i in items] == ["train", "test"]
    assert items[0].path.name == "train_00001_aligned.jpg"


def test_kdef_reader(tmp_path, labels):
    d = tmp_path / "AF01"
    d.mkdir()
    for n in ["AF01ANS.JPG", "AF01HAS.JPG", "AF01HAHL.JPG", "readme.txt"]:
        (d / n).write_bytes(b"")
    items = list(iter_kdef(tmp_path, labels))
    assert sorted(i.label for i in items) == ["angry", "happy"]
    assert all(i.subject == "F01" for i in items)


# ---------------------------------------------------------------- 特征缓存与归一化


def test_npz_roundtrip_and_reorder(tmp_path, canonical):
    rng = np.random.default_rng(0)
    x = rng.random((5, 51)).astype(np.float32)
    names = list(canonical.names)
    perm = rng.permutation(51)
    save_features(tmp_path / "f.npz", x[:, perm], ["happy"] * 5, [""] * 5, ["train"] * 5, [f"r{i}" for i in range(5)],
                  [names[i] for i in perm], {"dataset": "demo"})
    fs = load_features(tmp_path / "f.npz", canonical)
    assert np.allclose(fs.x, x)  # 维度按名字重排回规范顺序
    assert fs.dataset == "demo" and list(fs.label) == ["happy"] * 5


def test_baseline_normalization():
    x = np.array([[0.2, 0.0], [0.2, 0.1], [0.6, 0.55]], dtype=np.float32)
    y = np.array([0, 0, 1])
    b = neutral_baseline(x, y, 0)
    assert np.allclose(b, [0.2, 0.05])
    xn = apply_baseline(x, b)
    assert np.allclose(xn[2], [(0.6 - 0.2) / 0.8, (0.55 - 0.05) / 0.95])
    assert xn.min() >= 0 and np.allclose(xn[0], [0.0, 0.0])


def test_split_by_subject_no_overlap():
    rng = np.random.default_rng(0)
    subj = np.array([f"S{i % 20:02d}" for i in range(400)])
    tr, va, how = split_train_val(np.full(400, ""), subj, "auto", 0.2, rng)
    assert how == "subject"
    assert not set(subj[tr]) & set(subj[va])
    assert tr.sum() + va.sum() == 400


def test_split_official_excludes_test():
    rng = np.random.default_rng(0)
    split = np.array(["train"] * 50 + ["val"] * 10 + ["test"] * 10)
    tr, va, how = split_train_val(split, np.full(70, ""), "auto", 0.2, rng)
    assert how == "official" and tr.sum() == 50 and va.sum() == 10
    split2 = np.array(["train"] * 50 + ["test"] * 10)
    tr2, va2, how2 = split_train_val(split2, np.full(60, ""), "auto", 0.2, rng)
    assert how2 == "official+random" and not (tr2 | va2)[50:].any()


def test_jaffe_reader(tmp_path, labels):
    from exprnet.datasets import iter_jaffe

    for n in ["KA.AN1.39.tiff", "KA.FE2.46.tiff", "YM.NE3.51.tiff", "README"]:
        (tmp_path / n).write_bytes(b"")
    items = list(iter_jaffe(tmp_path, labels))
    assert [i.label for i in items] == ["angry", "fear", "neutral"]
    assert [i.subject for i in items] == ["KA", "KA", "YM"]
