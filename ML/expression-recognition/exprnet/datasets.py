"""数据：公开集读取（imagefolder / FER2013 csv / RAF-DB 列表 / AffectNet csv / CK+ / KDEF）、
特征缓存 npz、基线归一化、训练 / 验证划分，以及把多个来源拼成训练数据（DESIGN §3）。

特征缓存 npz 的字段：
    x        [N,51] float32  规范空间顺序的表情基系数（MediaPipe 原值，未做基线归一化）
    label    [N] str          归一后的类别 key（可能是未启用的，如 contempt；训练时再按 labels.yaml 过滤）
    subject  [N] str          受试者编号（没有则为空串）
    split    [N] str          官方划分 train / val / test（没有则为空串）
    path     [N] str          样本在数据集根目录下的相对路径（FER2013 为行号）
    names    [51] str         维度名，读取时与 canonical 核对
    meta     json 字符串      数据集名、抽取参数、检出率等
"""

from __future__ import annotations

import csv
import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator

import numpy as np

from .canonical import Canonical
from .common import CACHE_DIR, Labels, array_hash, rel_to_project, resolve_path

IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"}

# 各公开集的授权说明（写进训练产物与导出元数据）。详见 REFERENCES.md §7。
DATASET_LICENSES = {
    "fer2013": "无官方许可证：图片由网络搜索抓取、版权来源不明，风险高于其余公开集；仅可做非商用研究原型",
    "rafdb": "使用协议：仅限非商用研究，不得转让、转售或盈利",
    "affectnet": "官方声明：仅限研究用途（research purposes only）",
    "ckplus": "签署使用协议获取；按惯例仅限非商用研究",
    "kdef": "使用条款：仅限非商用科研，不得再分发，不得在 App / 在线服务中公开使用",
    "jaffe": "仅限非商用科研，禁止再分发",
}
UNKNOWN_LICENSE = "授权未知，按仅限非商用处理"


# ---------------------------------------------------------------- 读取公开集（给 extract 用）


@dataclass
class Item:
    """一个待抽取样本。image 与 path 二选一（FER2013 直接给像素）。"""

    label: str | None  # 归一后的类别 key；None = 标签不认识
    subject: str = ""
    split: str = ""
    path: Path | None = None
    image: np.ndarray | None = None
    ref: str = ""  # 报告与 npz 里记录的相对引用


def _norm_split(s: str) -> str:
    s = s.strip().lower()
    if s in ("train", "training"):
        return "train"
    if s in ("val", "valid", "validation", "publictest"):
        return "val"
    if s in ("test", "privatetest", "testing"):
        return "test"
    return ""


def iter_imagefolder(root: Path, labels: Labels, subject_regex: str | None = None) -> Iterator[Item]:
    """root/<标签>/**/图片，或 root/<train|val|test>/<标签>/**/图片。子文件夹名经 aliases 归一。"""
    root = Path(root)
    splits = [d for d in root.iterdir() if d.is_dir() and _norm_split(d.name)]
    groups = [(d, _norm_split(d.name)) for d in splits] if splits else [(root, "")]
    rx = re.compile(subject_regex) if subject_regex else None
    for base, split in sorted(groups):
        for label_dir in sorted(d for d in base.iterdir() if d.is_dir()):
            key = labels.normalize(label_dir.name)
            for p in sorted(label_dir.rglob("*")):
                if p.suffix.lower() not in IMAGE_EXTS:
                    continue
                subject = ""
                if rx:
                    m = rx.search(p.name)
                    subject = m.group(1) if m else ""
                yield Item(label=key, subject=subject, split=split, path=p, ref=p.relative_to(root).as_posix())


def iter_fer2013(csv_path: Path, labels: Labels) -> Iterator[Item]:
    """fer2013.csv：emotion,pixels,Usage。像素是 48×48 灰度，空格分隔。"""
    with open(csv_path, "r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        for i, row in enumerate(reader):
            img = np.array(row["pixels"].split(), dtype=np.uint8).reshape(48, 48)
            key = labels.dataset_label("fer2013", int(row["emotion"]))
            yield Item(label=key, split=_norm_split(row.get("Usage", "")), image=img, ref=f"row{i}")


def iter_rafdb(root: Path, labels: Labels, variant: str = "auto") -> Iterator[Item]:
    """RAF-DB basic：EmoLabel/list_patition_label.txt（每行「文件名 编号」）+ Image/aligned 或 Image/original。"""
    root = Path(root)
    lst = root / "EmoLabel" / "list_patition_label.txt"
    if not lst.exists():
        raise FileNotFoundError(f"找不到 RAF-DB 标签文件：{lst.name}（应在 <根>/EmoLabel/ 下）")
    aligned = root / "Image" / "aligned"
    original = root / "Image" / "original"
    if variant == "auto":
        variant = "aligned" if aligned.exists() else "original"
    for line in lst.read_text(encoding="utf-8").splitlines():
        parts = line.split()
        if len(parts) < 2:
            continue
        name, code = parts[0], int(parts[1])
        if variant == "aligned":
            p = aligned / (Path(name).stem + "_aligned" + Path(name).suffix)
        else:
            p = original / name
        split = "train" if name.startswith("train") else ("test" if name.startswith("test") else "")
        yield Item(label=labels.dataset_label("rafdb", code), split=split, path=p, ref=p.relative_to(root).as_posix())


def iter_affectnet(root: Path, labels: Labels) -> Iterator[Item]:
    """AffectNet 官方发布格式：Manually_Annotated_file_lists/{training,validation}.csv + Manually_Annotated_Images/。
    官方 validation 集在文献里当测试集用，这里记为 test。"""
    root = Path(root)
    lists = root / "Manually_Annotated_file_lists"
    img_root = root / "Manually_Annotated_Images"
    for fname, split in (("training.csv", "train"), ("validation.csv", "test")):
        f = lists / fname
        if not f.exists():
            continue
        with open(f, "r", encoding="utf-8", newline="") as fh:
            for row in csv.DictReader(fh):
                p = img_root / row["subDirectory_filePath"]
                key = labels.dataset_label("affectnet", int(row["expression"]))
                yield Item(label=key, split=split, path=p, ref=p.relative_to(root).as_posix())


def iter_ckplus(root: Path, labels: Labels) -> Iterator[Item]:
    """CK+：cohn-kanade-images/Sxxx/yyy/*.png + Emotion/Sxxx/yyy/*_emotion.txt。
    有情绪标签的序列取末帧（峰值）为该情绪、首帧为中性；受试者编号 Sxxx 用于按人划分。"""
    root = Path(root)
    img_root = root / "cohn-kanade-images"
    emo_root = root / "Emotion"
    for emo_file in sorted(emo_root.rglob("*_emotion.txt")):
        seq_dir = img_root / emo_file.parent.relative_to(emo_root)
        frames = sorted(p for p in seq_dir.glob("*.png"))
        if not frames:
            continue
        code = int(float(emo_file.read_text(encoding="utf-8").strip().split()[0]))
        subject = emo_file.parent.parent.name
        yield Item(label=labels.dataset_label("ckplus", code), subject=subject, path=frames[-1], ref=frames[-1].relative_to(root).as_posix())
        yield Item(label=labels.dataset_label("ckplus", 0), subject=subject, path=frames[0], ref=frames[0].relative_to(root).as_posix())


_KDEF_RX = re.compile(r"^([AB])([FM])(\d\d)(AF|AN|DI|HA|NE|SA|SU)(S|HL|HR|FL|FR)\.(jpe?g|png)$", re.IGNORECASE)


def iter_kdef(root: Path, labels: Labels, angles: tuple[str, ...] = ("S",)) -> Iterator[Item]:
    """KDEF：文件名如 AF01ANS.JPG = 场次 A、女、01 号、愤怒、正面。默认只取正面（S）。"""
    root = Path(root)
    for p in sorted(root.rglob("*")):
        m = _KDEF_RX.match(p.name)
        if not m or m.group(5).upper() not in angles:
            continue
        subject = m.group(2).upper() + m.group(3)
        yield Item(label=labels.normalize(m.group(4)), subject=subject, path=p, ref=p.relative_to(root).as_posix())


_JAFFE_RX = re.compile(r"^([A-Z]{2})\.([A-Z]{2})\d*\.\d+\.(tiff?|jpe?g|png)$", re.IGNORECASE)


def iter_jaffe(root: Path, labels: Labels) -> Iterator[Item]:
    """JAFFE：文件名如 KA.AN1.39.tiff = 受试者 KA、愤怒。表情码 AN DI FE HA NE SA SU。"""
    root = Path(root)
    for p in sorted(root.rglob("*")):
        m = _JAFFE_RX.match(p.name)
        if not m:
            continue
        yield Item(label=labels.normalize(m.group(2)), subject=m.group(1).upper(), path=p, ref=p.relative_to(root).as_posix())


READERS = {
    "imagefolder": iter_imagefolder,
    "fer2013": iter_fer2013,
    "rafdb": iter_rafdb,
    "affectnet": iter_affectnet,
    "ckplus": iter_ckplus,
    "kdef": iter_kdef,
    "jaffe": iter_jaffe,
}


# ---------------------------------------------------------------- 特征缓存 npz


def save_features(path: str | Path, x: np.ndarray, label, subject, split, ref, names: list[str], meta: dict) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(
        path,
        x=np.asarray(x, dtype=np.float32).reshape(-1, len(names)),
        label=np.asarray(label, dtype=str),
        subject=np.asarray(subject, dtype=str),
        split=np.asarray(split, dtype=str),
        path=np.asarray(ref, dtype=str),
        names=np.asarray(names, dtype=str),
        meta=np.asarray(json.dumps(meta, ensure_ascii=False)),
    )


@dataclass
class FeatureSet:
    x: np.ndarray
    label: np.ndarray
    subject: np.ndarray
    split: np.ndarray
    ref: np.ndarray
    meta: dict
    path: Path

    @property
    def dataset(self) -> str:
        return str(self.meta.get("dataset", self.path.stem))


def load_features(path: str | Path, canonical: Canonical) -> FeatureSet:
    path = Path(path)
    z = np.load(path, allow_pickle=False)
    names = [str(n) for n in z["names"]]
    x = z["x"].astype(np.float32)
    if names != canonical.names:
        # 按名字对齐（不按下标）：缓存来自别的维度顺序时重排
        idx = {n: i for i, n in enumerate(names)}
        missing = [n for n in canonical.names if n not in idx]
        if missing:
            raise ValueError(f"{path.name} 缺少规范空间维：{missing[:5]}…")
        x = x[:, [idx[n] for n in canonical.names]]
    meta = json.loads(str(z["meta"])) if "meta" in z else {}
    n = len(x)

    def _get(k):
        return z[k].astype(str) if k in z else np.full(n, "", dtype=str)

    return FeatureSet(x=x, label=_get("label"), subject=_get("subject"), split=_get("split"), ref=_get("path"), meta=meta, path=path)


# ---------------------------------------------------------------- 基线归一化（DESIGN §3.2）


def neutral_baseline(x: np.ndarray, y: np.ndarray, neutral_idx: int | None) -> np.ndarray:
    """b = 中性类样本的逐维中位数；没有中性样本时返回全 0。b 限制在 [0, 0.95]。"""
    if neutral_idx is None or not np.any(y == neutral_idx):
        return np.zeros(x.shape[1], dtype=np.float32)
    b = np.median(x[y == neutral_idx], axis=0)
    return np.clip(b, 0.0, 0.95).astype(np.float32)


def apply_baseline(x: np.ndarray, b: np.ndarray) -> np.ndarray:
    """x' = clip((x − b) / (1 − b), 0, 1)。"""
    return np.clip((x - b) / (1.0 - b), 0.0, 1.0).astype(np.float32)


# ---------------------------------------------------------------- 划分


def split_train_val(
    split: np.ndarray, subject: np.ndarray, mode: str, val_fraction: float, rng: np.random.Generator
) -> tuple[np.ndarray, np.ndarray, str]:
    """返回 (train_mask, val_mask, 实际使用的划分方式)。官方 test 划分的样本两边都不进（留给 evaluate）。

    mode: auto | official | subject | random
      official：用 split 列的 train / val；没有 val 时从 train 里再按人或随机切出验证集
      subject ：按受试者编号划分，同一人不跨训练 / 验证
    """
    n = len(split)
    has_split = np.any(split != "")
    has_subject = np.any(subject != "")
    if mode == "auto":
        mode = "official" if has_split else ("subject" if has_subject else "random")
    pool = np.ones(n, dtype=bool)
    if mode == "official":
        if not has_split:
            raise ValueError("要求按官方划分，但数据里没有 split 列")
        pool = split == "train"
        if np.any(split == "val"):
            return pool, split == "val", "official"
        # 没有官方验证集：在 train 里再切
        sub_mode = "subject" if has_subject else "random"
        tr, va, _ = split_train_val(np.full(n, "", dtype=str), subject, sub_mode, val_fraction, rng)
        return tr & pool, va & pool, f"official+{sub_mode}"
    if mode == "subject":
        if not has_subject:
            raise ValueError("要求按人划分，但数据里没有受试者编号")
        subs = np.unique(subject)
        rng.shuffle(subs)
        n_val = max(1, int(round(len(subs) * val_fraction)))
        val_subs = set(subs[:n_val].tolist())
        va = np.array([s in val_subs for s in subject])
        return ~va & pool, va & pool, "subject"
    if mode == "random":
        va = rng.random(n) < val_fraction
        return ~va & pool, va & pool, "random"
    raise ValueError(f"未知划分方式：{mode}")


# ---------------------------------------------------------------- 拼训练数据


@dataclass
class TrainData:
    """训练所需的全部数组。x* 为原值，xp* 为绑定投影重建（单位绑定时与原值相同）。"""

    x_train: np.ndarray
    xp_train: np.ndarray
    y_train: np.ndarray  # [N,C] 软标签
    real_train: np.ndarray  # [N] bool，True = 公开集特征
    x_val: np.ndarray
    xp_val: np.ndarray
    y_val: np.ndarray  # [M] int
    src_val: np.ndarray  # [M] int，来源下标
    neg_train: np.ndarray
    negp_train: np.ndarray
    neg_heldout: np.ndarray
    negp_heldout: np.ndarray
    neg_calib: np.ndarray  # 标定用负样本：只用来定 TNR95 阈值
    negp_calib: np.ndarray
    sources: list[dict] = field(default_factory=list)
    negatives_info: dict = field(default_factory=dict)  # 三批负样本的数量与种子
    class_prior: np.ndarray | None = None

    @property
    def commercial_use_allowed(self) -> bool:
        return all(s["kind"] == "synthetic" for s in self.sources)


def project_cached(rig, x: np.ndarray, mask: np.ndarray, use_cache: bool = True) -> np.ndarray:
    """绑定投影并按「数据 + 绑定哈希 + 屏蔽」缓存到 .cache/projection/，避免每次训练重解。"""
    if rig.identity:
        return (np.clip(x, 0, 1) * mask).astype(np.float32)
    from .rig import PROJECTION_VERSION

    key = array_hash(x.astype(np.float32), mask.astype(np.float32)) + "_" + rig.config_hash()[:16] + f"_v{PROJECTION_VERSION}"
    cache = CACHE_DIR / "projection" / f"{key[:24]}_{key[-20:]}.npz"
    if use_cache and cache.exists():
        return np.load(cache)["x_hat"]
    _, x_hat = rig.project(x, mask)
    if use_cache:
        cache.parent.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(cache, x_hat=x_hat)
    return x_hat


def negative_seeds(seed: int, ncfg: dict) -> dict[str, int]:
    """三批负样本（训练 / 标定 / 留出）的种子，互不相同，否则报错。"""
    off = {"train": 101, "heldout": 202, "calib": 303}
    off.update({k: int(v) for k, v in (ncfg.get("seed_offsets") or {}).items()})
    seeds = {k: int(seed) + off[k] for k in ("train", "calib", "heldout")}
    if len(set(seeds.values())) != 3:
        raise ValueError(f"训练 / 标定 / 留出负样本的种子必须互不相同：{seeds}")
    return seeds


def build_train_data(cfg: dict, canonical: Canonical, labels: Labels, synth, rig, log=print, use_cache: bool = True) -> TrainData:
    """按 train.yaml 的 data 段拼出训练 / 验证 / 负样本数组。"""
    dcfg = cfg["data"]
    seed = int(cfg.get("seed", 0))
    rng = np.random.default_rng(seed)
    C = labels.num_classes
    mask = canonical.mask_vector
    neutral_idx = labels.keys.index("neutral") if "neutral" in labels.keys else None
    val_fraction = float(dcfg.get("val_fraction", 0.15))

    xs_tr, ys_tr, real_tr, xs_va, ys_va, src_va = [], [], [], [], [], []
    sources: list[dict] = []
    for si, src in enumerate(dcfg["sources"]):
        kind = src["kind"]
        if kind == "synthetic":
            n = int(src.get("n_per_class", 1000))
            s_seed = int(src.get("seed", seed))
            X, y, clean = synth.generate(n, s_seed, return_clean=True)
            va = np.random.default_rng(s_seed + 1).random(len(X)) < val_fraction
            xs_tr.append(X[~va]); ys_tr.append(y[~va]); real_tr.append(np.zeros((~va).sum(), bool))
            xs_va.append(X[va]); ys_va.append(y[va]); src_va.append(np.full(va.sum(), si))
            sources.append({"kind": "synthetic", "name": "facs_synthetic", "n_per_class": n, "seed": s_seed,
                            "clean_fraction": synth.clean_fraction, "n_clean": int(clean.sum()),
                            "n_train": int((~va).sum()), "n_val": int(va.sum()),
                            "license": "自有（FACS 原型合成，无第三方数据）", "split": "random"})
            log(f"[数据] 合成：每类 {n}（其中干净稀疏样本 {clean.sum()}，占 {clean.mean():.0%}），训练 {(~va).sum()}，验证 {va.sum()}")
        elif kind == "npz":
            path = resolve_path(src["path"])
            fs = load_features(path, canonical)
            y_all = np.array([labels.to_class_index(l) if l else None for l in fs.label], dtype=object)
            keep = np.array([v is not None for v in y_all])
            y = np.array([v for v in y_all[keep]], dtype=np.int64)
            X = fs.x[keep]
            tr, va, how = split_train_val(fs.split[keep], fs.subject[keep], src.get("split", "auto"), val_fraction, rng)
            b = neutral_baseline(X[tr], y[tr], neutral_idx) if src.get("baseline_norm", True) else np.zeros(X.shape[1], np.float32)
            X = apply_baseline(X, b)
            w = float(src.get("weight", 1.0))
            idx_tr = np.flatnonzero(tr)
            if w != 1.0 and len(idx_tr):  # 按权重重复采样（>1 过采样，<1 欠采样）
                idx_tr = rng.choice(idx_tr, size=max(1, int(round(len(idx_tr) * w))), replace=w > 1.0)
            xs_tr.append(X[idx_tr]); ys_tr.append(y[idx_tr]); real_tr.append(np.ones(len(idx_tr), bool))
            xs_va.append(X[va]); ys_va.append(y[va]); src_va.append(np.full(va.sum(), si))
            ds = fs.dataset
            sources.append({"kind": "npz", "name": ds, "path": rel_to_project(path), "n_train": int(len(idx_tr)),
                            "n_val": int(va.sum()), "n_dropped_label": int((~keep).sum()), "split": how,
                            "baseline": b.round(4).tolist(), "license": DATASET_LICENSES.get(ds, UNKNOWN_LICENSE)})
            log(f"[数据] {ds}：训练 {len(idx_tr)}，验证 {va.sum()}，划分 {how}，丢弃未启用标签 {(~keep).sum()}")
        else:
            raise ValueError(f"未知数据来源类型：{kind}")

    x_train = np.concatenate(xs_tr).astype(np.float32)
    y_hard = np.concatenate(ys_tr)
    y_train = np.eye(C, dtype=np.float32)[y_hard]
    real_train = np.concatenate(real_tr)
    x_val = np.concatenate(xs_va).astype(np.float32)
    y_val = np.concatenate(ys_va)
    src_val = np.concatenate(src_va)
    prior = np.bincount(y_hard, minlength=C).astype(np.float64) + 1.0
    prior /= prior.sum()

    ncfg = dcfg.get("negatives", {})
    neg_seeds = negative_seeds(seed, ncfg)
    n_heldout = int(ncfg.get("heldout", 2000))
    n_calib = int(ncfg["calib"]) if ncfg.get("calib") is not None else n_heldout
    # 「离正样本太近」的参照集由合成器按 facs.yaml 的 reference_per_class 现场合成（与训练正样本不同种子）
    neg_train = synth.negatives(int(ncfg.get("train", 5000)), neg_seeds["train"], rig=rig)
    neg_heldout = synth.negatives(n_heldout, neg_seeds["heldout"], rig=rig)
    neg_calib = synth.negatives(n_calib, neg_seeds["calib"], rig=rig)
    log(f"[数据] 负样本：训练 {len(neg_train)}（种子 {neg_seeds['train']}），标定 {len(neg_calib)}（种子 {neg_seeds['calib']}），"
        f"留出 {len(neg_heldout)}（种子 {neg_seeds['heldout']}）")
    negatives_info = {
        "train": {"n": len(neg_train), "seed": neg_seeds["train"], "use": "Outlier Exposure 训练"},
        "calib": {"n": len(neg_calib), "seed": neg_seeds["calib"], "use": "只用来定 TNR95 阈值"},
        "heldout": {"n": len(neg_heldout), "seed": neg_seeds["heldout"], "use": "只用来报 OOD 指标与拒识率"},
    }

    if rig.identity:
        log("[数据] 单位绑定，不做投影")
    else:
        log(f"[数据] 绑定投影（{rig.name}，{rig.num_sliders} 根滑杆），结果缓存到 .cache/projection/ …")
    xp_train = project_cached(rig, x_train, mask, use_cache)
    xp_val = project_cached(rig, x_val, mask, use_cache)
    negp_train = project_cached(rig, neg_train, mask, use_cache)
    negp_heldout = project_cached(rig, neg_heldout, mask, use_cache)
    negp_calib = project_cached(rig, neg_calib, mask, use_cache)

    return TrainData(
        x_train=x_train, xp_train=xp_train, y_train=y_train, real_train=real_train,
        x_val=x_val, xp_val=xp_val, y_val=y_val, src_val=src_val,
        neg_train=neg_train, negp_train=negp_train, neg_heldout=neg_heldout, negp_heldout=negp_heldout,
        neg_calib=neg_calib, negp_calib=negp_calib, negatives_info=negatives_info,
        sources=sources, class_prior=prior.astype(np.float32),
    )
