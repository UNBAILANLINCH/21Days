"""从图片抽 MediaPipe 表情基特征 → 特征缓存 npz（DESIGN §2、§3.1）。

MediaPipe Face Landmarker（Tasks API）每张脸返回 52 项 blendshape，第一项是 `_neutral`，没有 `tongueOut`；
ARKit 原版 52 项则含 `tongueOut`、没有 `_neutral`。这里**按名字**对齐到规范空间 51 维，不按下标。

依赖 requirements-extract.txt（mediapipe、opencv-python-headless），只在抽特征时需要；
模型文件 face_landmarker.task 默认放 .cache/，可用 --download-model 下载。

用法示例（在 ML/expression-recognition 目录下）：
    python -m exprnet.extract --source fer2013 --input data/raw/fer2013/fer2013.csv --out data/features/fer2013.npz
    python -m exprnet.extract --source rafdb --input data/raw/rafdb --out data/features/rafdb.npz
    python -m exprnet.extract --source ckplus --input data/raw/ckplus --out data/features/ckplus.npz
    python -m exprnet.extract --source imagefolder --input data/raw/jaffe --out data/features/jaffe.npz --dataset-name jaffe
"""

from __future__ import annotations

import sys
import time
import urllib.request
from pathlib import Path
from typing import Iterable

import numpy as np

from .canonical import Canonical, load_canonical
from .common import make_parser, CACHE_DIR, load_labels, rel_to_project, resolve_cli_path, save_json
from .datasets import READERS, Item, save_features

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task"
DEFAULT_MODEL = CACHE_DIR / "face_landmarker.task"

# MediaPipe Tasks API 的输出类别名与顺序（face_blendshapes_graph.cc 的 kBlendshapeNames，已对照 mediapipe 1.0.1 核实）
MEDIAPIPE_BLENDSHAPE_NAMES = [
    "_neutral", "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
    "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "eyeBlinkLeft", "eyeBlinkRight",
    "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight",
    "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight",
    "jawForward", "jawLeft", "jawOpen", "jawRight", "mouthClose", "mouthDimpleLeft", "mouthDimpleRight",
    "mouthFrownLeft", "mouthFrownRight", "mouthFunnel", "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight",
    "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper",
    "mouthShrugLower", "mouthShrugUpper", "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
    "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft", "noseSneerRight",
]
# 两种口径里各自多出来的「非表情基」项，对齐时丢掉
IGNORED_NAMES = {"_neutral", "tongueOut"}

DEFAULT_UPSCALE = {"fer2013": 4, "rafdb": 2}  # FER2013 48×48 → 192×192；RAF-DB aligned 100×100 → 200×200


def categories_to_vector(categories, canonical: Canonical) -> np.ndarray:
    """把一张脸的 blendshape 输出按名字对齐成规范空间向量 [51]。

    categories 可以是 MediaPipe 的 Category 列表（有 category_name / score）、(名字, 分数) 列表或 {名字: 分数}。
    会忽略 `_neutral` 与 `tongueOut`；缺规范空间里的任何一维都报错。
    """
    if isinstance(categories, dict):
        pairs = categories.items()
    else:
        pairs = []
        for c in categories:
            if isinstance(c, (tuple, list)):
                pairs.append((c[0], c[1]))
            else:
                pairs.append((c.category_name, c.score))
    scores = {str(n): float(s) for n, s in pairs if str(n) not in IGNORED_NAMES}
    missing = [n for n in canonical.names if n not in scores]
    if missing:
        raise ValueError(f"blendshape 输出缺少规范空间维：{missing}")
    unknown = sorted(set(scores) - set(canonical.names))
    if unknown:
        raise ValueError(f"blendshape 输出里有规范空间不认识的名字：{unknown}")
    return np.array([scores[n] for n in canonical.names], dtype=np.float32)


class MediaPipeExtractor:
    """MediaPipe Face Landmarker（IMAGE 模式、单脸、输出 blendshape）。"""

    def __init__(self, model_path: str | Path = DEFAULT_MODEL, min_detection_confidence: float = 0.3):
        try:
            import mediapipe as mp
            from mediapipe.tasks.python import vision
            from mediapipe.tasks.python.core import base_options
        except ImportError as e:
            raise ImportError("没装 mediapipe：pip install -r requirements-extract.txt") from e
        model_path = Path(model_path)
        if not model_path.exists():
            raise FileNotFoundError(f"找不到 MediaPipe 模型 {model_path.name}；加 --download-model 下载到 .cache/，或手动从 {MODEL_URL} 下载")
        self._mp = mp
        opts = vision.FaceLandmarkerOptions(
            base_options=base_options.BaseOptions(model_asset_path=str(model_path)),
            running_mode=vision.RunningMode.IMAGE,
            num_faces=1,
            min_face_detection_confidence=min_detection_confidence,
            min_face_presence_confidence=min_detection_confidence,
            output_face_blendshapes=True,
        )
        self._lm = vision.FaceLandmarker.create_from_options(opts)

    def __call__(self, rgb: np.ndarray):
        img = self._mp.Image(image_format=self._mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb, dtype=np.uint8))
        res = self._lm.detect(img)
        if not res.face_blendshapes:
            return None
        return res.face_blendshapes[0]

    def close(self) -> None:
        self._lm.close()


def download_model(dest: Path = DEFAULT_MODEL) -> Path:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"[抽取] 下载 MediaPipe 模型 → {rel_to_project(dest)}")
    urllib.request.urlretrieve(MODEL_URL, dest)
    return dest


def to_rgb(img: np.ndarray, upscale: float = 1.0) -> np.ndarray:
    """灰度 / BGR / RGB 统一成 RGB uint8，并按需放大（双三次）。"""
    import cv2

    if img.ndim == 2:
        img = np.stack([img] * 3, axis=-1)
    elif img.shape[2] == 4:
        img = img[:, :, :3]
    if upscale and upscale != 1.0:
        h, w = img.shape[:2]
        img = cv2.resize(img, (int(round(w * upscale)), int(round(h * upscale))), interpolation=cv2.INTER_CUBIC)
    return np.ascontiguousarray(img.astype(np.uint8))


def load_item_rgb(item: Item) -> np.ndarray | None:
    if item.image is not None:
        return item.image
    import cv2

    data = np.fromfile(str(item.path), dtype=np.uint8)  # 兼容非 ASCII 路径
    bgr = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if bgr is None:
        return None
    return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)


def extract_items(items: Iterable[Item], extractor, canonical: Canonical, upscale: float = 1.0, limit: int | None = None,
                  log=print, image_loader=load_item_rgb, preprocess=to_rgb) -> dict:
    """逐张抽取。extractor(rgb) 返回 blendshape 类别列表或 None（没检出人脸）。

    返回 {x, label, subject, split, ref, stats}；stats 里有总体与逐类检出率。
    """
    xs, labels, subjects, splits, refs = [], [], [], [], []
    per_label: dict[str, list[int]] = {}
    n_total = n_detect = n_unreadable = n_unlabeled = 0
    t0 = time.time()
    for item in items:
        if limit is not None and n_total >= limit:
            break
        if item.label is None:
            n_unlabeled += 1
            continue
        n_total += 1
        stat = per_label.setdefault(item.label, [0, 0])
        stat[1] += 1
        img = image_loader(item)
        if img is None:
            n_unreadable += 1
            continue
        cats = extractor(preprocess(img, upscale))
        if cats is None:
            continue
        xs.append(categories_to_vector(cats, canonical))
        labels.append(item.label)
        subjects.append(item.subject)
        splits.append(item.split)
        refs.append(item.ref)
        n_detect += 1
        stat[0] += 1
        if n_total % 500 == 0:
            log(f"[抽取] 已处理 {n_total}，检出 {n_detect}（{n_detect / n_total:.1%}），{time.time() - t0:.0f}s")
    stats = {
        "n_total": n_total,
        "n_detected": n_detect,
        "detection_rate": n_detect / n_total if n_total else 0.0,
        "n_unreadable": n_unreadable,
        "n_unlabeled_skipped": n_unlabeled,
        "per_label": {k: {"detected": v[0], "total": v[1], "rate": v[0] / v[1] if v[1] else 0.0} for k, v in sorted(per_label.items())},
        "upscale": upscale,
        "seconds": time.time() - t0,
    }
    D = canonical.dim
    return {
        "x": np.array(xs, dtype=np.float32).reshape(-1, D),
        "label": labels, "subject": subjects, "split": splits, "ref": refs, "stats": stats,
    }


def main(argv=None) -> int:
    ap = make_parser("python -m exprnet.extract", "用 MediaPipe Face Landmarker 从公开集图片抽 51 维表情基特征，写成特征缓存 npz，并报告检出率。")
    ap.add_argument("--source", required=True, choices=sorted(READERS), help="数据集格式")
    ap.add_argument("--input", required=True, help="数据集根目录（fer2013 为 csv 文件路径）")
    ap.add_argument("--out", required=True, help="输出 npz 路径，如 data/features/rafdb.npz")
    ap.add_argument("--dataset-name", default=None, help="写进元数据的数据集名（默认同 --source；imagefolder 建议写 jaffe / kdef 等，用于授权说明）")
    ap.add_argument("--model", default=None, help="face_landmarker.task 路径，默认 .cache/face_landmarker.task")
    ap.add_argument("--download-model", action="store_true", help="模型不存在时从官方地址下载到 .cache/")
    ap.add_argument("--upscale", type=float, default=None, help="送检前放大倍数（默认 fer2013=4、rafdb=2、其他=1）")
    ap.add_argument("--min-confidence", type=float, default=0.3, help="人脸检出置信度下限（默认 0.3）")
    ap.add_argument("--limit", type=int, default=None, help="只处理前 N 张（试跑用）")
    ap.add_argument("--subject-regex", default=None, help="imagefolder：从文件名提取受试者编号的正则（第 1 个分组）")
    ap.add_argument("--rafdb-variant", choices=["auto", "aligned", "original"], default="auto", help="RAF-DB 用对齐图还是原图")
    ap.add_argument("--kdef-angles", default="S", help="KDEF 取哪些角度，逗号分隔（S 正面、HL/HR 半侧、FL/FR 全侧）")
    args = ap.parse_args(argv)

    canonical = load_canonical(masked=[])
    labels = load_labels()
    src = Path(resolve_cli_path(args.input))
    reader = READERS[args.source]
    if args.source == "imagefolder":
        items = reader(src, labels, args.subject_regex)
    elif args.source == "rafdb":
        items = reader(src, labels, args.rafdb_variant)
    elif args.source == "kdef":
        items = reader(src, labels, tuple(a.strip().upper() for a in args.kdef_angles.split(",")))
    else:
        items = reader(src, labels)
    model = Path(resolve_cli_path(args.model)) if args.model else DEFAULT_MODEL
    if not model.exists() and args.download_model:
        download_model(model)
    extractor = MediaPipeExtractor(model, args.min_confidence)
    upscale = args.upscale if args.upscale is not None else DEFAULT_UPSCALE.get(args.source, 1.0)
    try:
        res = extract_items(items, extractor, canonical, upscale=upscale, limit=args.limit)
    finally:
        extractor.close()
    st = res["stats"]
    dataset = args.dataset_name or args.source
    out = Path(resolve_cli_path(args.out))
    meta = {"dataset": dataset, "source_format": args.source, "extractor": "mediapipe_face_landmarker",
            "model_file": model.name, "stats": st}
    save_features(out, res["x"], res["label"], res["subject"], res["split"], res["ref"], canonical.names, meta)
    save_json(meta, out.with_suffix(".extract.json"))
    print(f"[抽取] {dataset}：{st['n_detected']}/{st['n_total']} 张检出人脸（检出率 {st['detection_rate']:.1%}，放大 {upscale}×），"
          f"不可读 {st['n_unreadable']}，标签不认识跳过 {st['n_unlabeled_skipped']}")
    for k, v in st["per_label"].items():
        print(f"         {k:<10} {v['detected']:>6}/{v['total']:<6} {v['rate']:.1%}")
    print(f"[抽取] 写出 {rel_to_project(out)} 与 {rel_to_project(out.with_suffix('.extract.json'))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
