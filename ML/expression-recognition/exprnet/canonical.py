"""规范表情空间：ARKit 表情基 51 维（DESIGN §2）。

负责维度顺序、解剖分区、左右侧、左右配对（镜像置换）与默认屏蔽维。
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .common import CONFIG_DIR, load_yaml, resolve_path

REGIONS = ["brow", "eye", "cheek", "nose", "mouth", "jaw"]
SIDES = ["L", "R", "C"]
CANONICAL_DIM = 51


@dataclass
class Canonical:
    names: list[str]
    regions: list[str]
    sides: list[str]
    mirror_index: np.ndarray  # [D] int，x[..., mirror_index] 即左右互换
    masked: np.ndarray  # [D] bool，True = 被屏蔽（训练与推理都置零）

    # ------------------------------------------------------------ 基本属性
    @property
    def dim(self) -> int:
        return len(self.names)

    def index(self, name: str) -> int:
        try:
            return self._index[name]
        except KeyError as e:
            raise KeyError(f"规范空间里没有表情基「{name}」") from e

    def __post_init__(self) -> None:
        self._index = {n: i for i, n in enumerate(self.names)}

    @property
    def region_ids(self) -> np.ndarray:
        return np.array([REGIONS.index(r) for r in self.regions], dtype=np.int64)

    @property
    def side_ids(self) -> np.ndarray:
        return np.array([SIDES.index(s) for s in self.sides], dtype=np.int64)

    @property
    def pair_groups(self) -> np.ndarray:
        """共享权重的分组编号：左右成对的两维同组，中间维自成一组。"""
        groups = np.full(self.dim, -1, dtype=np.int64)
        g = 0
        for i in range(self.dim):
            if groups[i] >= 0:
                continue
            groups[i] = g
            j = int(self.mirror_index[i])
            if j != i:
                groups[j] = g
            g += 1
        return groups

    @property
    def mask_vector(self) -> np.ndarray:
        """[D] float32，1 = 保留，0 = 屏蔽。"""
        return (~self.masked).astype(np.float32)

    @property
    def active_indices(self) -> np.ndarray:
        return np.flatnonzero(~self.masked).astype(np.int64)

    @property
    def masked_names(self) -> list[str]:
        return [n for n, m in zip(self.names, self.masked) if m]

    # ------------------------------------------------------------ 变换
    def mirror(self, x: np.ndarray) -> np.ndarray:
        """左右互换（最后一维是规范空间维）。"""
        return x[..., self.mirror_index]

    def apply_mask(self, x: np.ndarray) -> np.ndarray:
        return x * self.mask_vector

    def vector(self, values: dict[str, float]) -> np.ndarray:
        """{表情基名: 值} → [D] 向量，没给的维为 0。"""
        v = np.zeros(self.dim, dtype=np.float32)
        for k, val in values.items():
            v[self.index(k)] = val
        return v

    def with_mask(self, masked_names: list[str]) -> "Canonical":
        m = np.zeros(self.dim, dtype=bool)
        for n in masked_names:
            m[self.index(n)] = True
        return Canonical(list(self.names), list(self.regions), list(self.sides), self.mirror_index.copy(), m)


def load_canonical(path: str | Path | None = None, masked: list[str] | None = None) -> Canonical:
    """读取 canonical.yaml 并做自洽性检查。masked 为 None 时用配置里的默认屏蔽维。"""
    path = resolve_path(path or CONFIG_DIR / "canonical.yaml")
    cfg = load_yaml(path)
    dims = cfg["dims"]
    names = [d["name"] for d in dims]
    if len(names) != CANONICAL_DIM:
        raise ValueError(f"规范空间应为 {CANONICAL_DIM} 维，实际 {len(names)} 维")
    if len(set(names)) != len(names):
        raise ValueError("规范空间里有重名表情基")
    for bad in ("_neutral", "tongueOut"):
        if bad in names:
            raise ValueError(f"规范空间不应包含 {bad}")
    regions = [d["region"] for d in dims]
    sides = [d["side"] for d in dims]
    for n, r, s in zip(names, regions, sides):
        if r not in REGIONS:
            raise ValueError(f"{n} 的 region「{r}」不在 {REGIONS} 里")
        if s not in SIDES:
            raise ValueError(f"{n} 的 side「{s}」不在 {SIDES} 里")
    idx = {n: i for i, n in enumerate(names)}
    mirror = np.arange(len(names), dtype=np.int64)
    for i, d in enumerate(dims):
        p = d.get("pair")
        if p is None:
            if sides[i] != "C":
                raise ValueError(f"{names[i]} 是 {sides[i]} 侧但没有写 pair")
            continue
        if p not in idx:
            raise ValueError(f"{names[i]} 的 pair「{p}」不存在")
        j = idx[p]
        if dims[j].get("pair") != names[i]:
            raise ValueError(f"{names[i]} 与 {p} 的配对不对称")
        if {sides[i], sides[j]} != {"L", "R"}:
            raise ValueError(f"{names[i]} 与 {p} 应一左一右")
        if regions[i] != regions[j]:
            raise ValueError(f"{names[i]} 与 {p} 的 region 不一致")
        mirror[i] = j
    masked_names = cfg.get("masked_by_default", []) if masked is None else masked
    m = np.zeros(len(names), dtype=bool)
    for n in masked_names:
        if n not in idx:
            raise ValueError(f"屏蔽维「{n}」不在规范空间里")
        m[idx[n]] = True
    return Canonical(names, regions, sides, mirror, m)
