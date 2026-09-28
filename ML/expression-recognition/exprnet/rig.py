"""捏脸绑定：滑杆 → 规范空间（DESIGN §3.2、§8、§9）。

前向（训练时的绑定重建与导出进 ONNX 的第一层是同一套公式）：
    s  = clip(sliders, min, max)
    x  = clip(relu(s) @ W_pos + relu(-s) @ W_neg, 0, 1)
其中 W_pos[k] = pos_k / max_k，W_neg[k] = neg_k / |min_k|，即滑杆拉满时恰好得到配置里写的激活值。

投影（给真人 / 合成样本找「最接近它的滑杆值」）：
    令 u = [p; n]，p_k = relu(s_k)/max_k ∈ [0,1]，n_k = relu(-s_k)/|min_k| ∈ [0,1]，
    解有界非负最小二乘 min ||A u - x||²，0 ≤ u ≤ 1（A 的列是各滑杆正 / 负半轴拉满时的表情基向量）；
    同一根滑杆不能同时往正负两边拉，出现 p_k>0 且 n_k>0 时逐根二选一再重解。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from scipy.optimize import lsq_linear, nnls

from .canonical import Canonical
from .common import load_yaml, resolve_path, stable_hash

_EPS = 1e-9
PROJECTION_VERSION = 2  # 投影算法有改动时加 1，让旧缓存失效


@dataclass
class Slider:
    key: str
    zh: str
    min: float
    max: float
    default: float = 0.0
    pos: dict[str, float] = field(default_factory=dict)
    neg: dict[str, float] = field(default_factory=dict)
    live2d: str | None = None

    def to_dict(self) -> dict:
        d = {
            "key": self.key,
            "zh": self.zh,
            "range": [float(self.min), float(self.max)],
            "default": float(self.default),
            "pos": {k: float(v) for k, v in self.pos.items()},
            "neg": {k: float(v) for k, v in self.neg.items()},
        }
        if self.live2d is not None:
            d["live2d"] = self.live2d
        return d


class Rig:
    """一套捏脸绑定。identity=True 表示单位绑定（输入就是规范空间 51 维）。"""

    def __init__(self, name: str, sliders: list[Slider], canonical: Canonical, identity: bool = False):
        self.name = name
        self.sliders = sliders
        self.canonical = canonical
        self.identity = identity
        self._validate()
        D = canonical.dim
        K = len(sliders)
        self.pos_basis = np.zeros((K, D), dtype=np.float64)  # 正半轴拉满时的表情基向量
        self.neg_basis = np.zeros((K, D), dtype=np.float64)
        for k, s in enumerate(sliders):
            for n, w in s.pos.items():
                self.pos_basis[k, canonical.index(n)] = w
            for n, w in s.neg.items():
                self.neg_basis[k, canonical.index(n)] = w
        self.s_min = np.array([s.min for s in sliders], dtype=np.float64)
        self.s_max = np.array([s.max for s in sliders], dtype=np.float64)
        self.defaults = np.array([s.default for s in sliders], dtype=np.float64)
        pos_scale = np.where(self.s_max > 0, 1.0 / np.maximum(self.s_max, _EPS), 0.0)
        neg_scale = np.where(self.s_min < 0, 1.0 / np.maximum(-self.s_min, _EPS), 0.0)
        self.W_pos = self.pos_basis * pos_scale[:, None]  # [K, D]，乘的是 relu(s)
        self.W_neg = self.neg_basis * neg_scale[:, None]  # [K, D]，乘的是 relu(-s)

    # ------------------------------------------------------------ 基本信息
    @property
    def num_sliders(self) -> int:
        return len(self.sliders)

    @property
    def keys(self) -> list[str]:
        return [s.key for s in self.sliders]

    def _validate(self) -> None:
        keys = [s.key for s in self.sliders]
        if len(set(keys)) != len(keys):
            raise ValueError(f"绑定 {self.name} 里滑杆 key 有重复")
        for s in self.sliders:
            if not s.min <= 0 <= s.max or s.min == s.max:
                raise ValueError(f"滑杆 {s.key} 的范围 [{s.min}, {s.max}] 必须包含 0 且非空")
            if not s.min <= s.default <= s.max:
                raise ValueError(f"滑杆 {s.key} 的默认值不在范围内")
            if s.pos and s.max <= 0:
                raise ValueError(f"滑杆 {s.key} 没有正半轴却写了 pos")
            if s.neg and s.min >= 0:
                raise ValueError(f"滑杆 {s.key} 没有负半轴却写了 neg")
            for n, w in list(s.pos.items()) + list(s.neg.items()):
                self.canonical.index(n)  # 名字不存在会抛错
                if w < 0:
                    raise ValueError(f"滑杆 {s.key} 对 {n} 的权重为负；负向请写在 neg 里")

    def to_config(self) -> dict:
        return {"name": self.name, "identity": self.identity, "sliders": [s.to_dict() for s in self.sliders]}

    def config_hash(self) -> str:
        """绑定配置哈希：覆盖所有滑杆字段与规范空间维度顺序；注释与排版不影响。"""
        return stable_hash({"rig": self.to_config(), "canonical": self.canonical.names})

    def slider_meta(self) -> list[dict]:
        return [
            {"key": s.key, "zh": s.zh, "min": float(s.min), "max": float(s.max), "default": float(s.default)}
            for s in self.sliders
        ]

    def reachable(self) -> np.ndarray:
        """[D] bool：有滑杆能驱动的表情基。"""
        return (self.pos_basis.sum(0) + self.neg_basis.sum(0)) > 0

    # ------------------------------------------------------------ 前向
    def forward(self, s: np.ndarray) -> np.ndarray:
        """滑杆值 [N,K] → 规范空间 [N,D]（float32）。"""
        s = np.clip(np.asarray(s, dtype=np.float64), self.s_min, self.s_max)
        x = np.maximum(s, 0) @ self.W_pos + np.maximum(-s, 0) @ self.W_neg
        return np.clip(x, 0.0, 1.0).astype(np.float32)

    def u_to_sliders(self, u: np.ndarray) -> np.ndarray:
        """u = [p; n]（[N, 2K]）→ 滑杆值 [N, K]。"""
        K = self.num_sliders
        p, n = u[..., :K], u[..., K:]
        return p * np.maximum(self.s_max, 0) - n * np.maximum(-self.s_min, 0)

    def basis_matrix(self) -> np.ndarray:
        """A = [pos_basis; neg_basis]^T，形状 [D, 2K]。"""
        return np.concatenate([self.pos_basis, self.neg_basis], axis=0).T

    def random_sliders(self, n: int, rng: np.random.Generator, active_fraction=(0.15, 0.6)) -> np.ndarray:
        """随机滑杆组合（随机稀疏度）：每个样本先抽稀疏度 f，每根滑杆以概率 f 激活并在范围内均匀取值。"""
        K = self.num_sliders
        f = rng.uniform(active_fraction[0], active_fraction[1], size=(n, 1))
        active = rng.random((n, K)) < f
        vals = rng.uniform(self.s_min, self.s_max, size=(n, K))
        return np.where(active, vals, 0.0)

    # ------------------------------------------------------------ 投影
    def project(self, X: np.ndarray, mask: np.ndarray | None = None) -> tuple[np.ndarray, np.ndarray]:
        """把规范空间样本投影到绑定可达的子空间。

        X: [N, D]；mask: [D] float（1 保留，0 屏蔽），被屏蔽维不参与拟合，重建结果里也置零。
        返回 (sliders [N,K], X_hat [N,D])，X_hat = forward(sliders) * mask。
        """
        X = np.asarray(X, dtype=np.float64)
        N, D = X.shape
        m = np.ones(D) if mask is None else np.asarray(mask, dtype=np.float64)
        if self.identity:
            S = np.clip(X, self.s_min, self.s_max)
            return S.astype(np.float32), (self.forward(S) * m).astype(np.float32)
        rows = m > 0
        A_full = self.basis_matrix()[rows]  # [D', 2K]
        col_ok = np.abs(A_full).sum(0) > 0  # 空列（没写 pos/neg，或只驱动屏蔽维）不参与求解
        K = self.num_sliders
        U = np.zeros((N, 2 * K))
        for i in range(N):
            U[i] = _project_one(A_full, X[i, rows], col_ok, K)
        S = self.u_to_sliders(U)
        X_hat = self.forward(S) * m.astype(np.float32)
        return S.astype(np.float32), X_hat.astype(np.float32)


def _bounded_nnls(A: np.ndarray, b: np.ndarray) -> np.ndarray:
    """min ||A u - b||，0 ≤ u ≤ 1。先试无上界 NNLS（快），越界再用 BVLS。"""
    if A.shape[1] == 0:
        return np.zeros(0)
    u, _ = nnls(A, b)
    if u.max(initial=0.0) <= 1.0 + 1e-9:
        return np.minimum(u, 1.0)
    res = lsq_linear(A, b, bounds=(0.0, 1.0), method="bvls")
    return np.clip(res.x, 0.0, 1.0)


def _solve_with_cols(A: np.ndarray, b: np.ndarray, allowed: np.ndarray) -> tuple[np.ndarray, float]:
    u = np.zeros(A.shape[1])
    idx = np.flatnonzero(allowed)
    if idx.size:
        u[idx] = _bounded_nnls(A[:, idx], b)
    r = A @ u - b
    return u, float(r @ r)


def _project_one(A: np.ndarray, b: np.ndarray, col_ok: np.ndarray, K: int, tol: float = 1e-6) -> np.ndarray:
    """单样本投影，处理「同一滑杆正负两侧同时为正」的冲突：逐根二选一，取残差小的一侧。"""
    allowed = col_ok.copy()
    u, res = _solve_with_cols(A, b, allowed)
    for _ in range(K):
        both = np.minimum(u[:K], u[K:])
        if both.max(initial=0.0) <= tol:
            break
        k = int(both.argmax())  # 先处理「两侧都用得最多」的那根
        best = None
        for drop in (k, K + k):  # 关掉正侧或负侧
            trial = allowed.copy()
            trial[drop] = False
            ut, rt = _solve_with_cols(A, b, trial)
            if best is None or rt < best[2]:
                best = (trial, ut, rt)
        allowed, u, res = best
    return u


# ---------------------------------------------------------------- 读取


def rig_from_dict(cfg: dict, canonical: Canonical) -> Rig:
    sliders = []
    for s in cfg["sliders"]:
        rng = s.get("range", [-1, 1])
        sliders.append(
            Slider(
                key=str(s["key"]),
                zh=str(s.get("zh", s["key"])),
                min=float(rng[0]),
                max=float(rng[1]),
                default=float(s.get("default", 0.0)),
                pos={str(k): float(v) for k, v in (s.get("pos") or {}).items()},
                neg={str(k): float(v) for k, v in (s.get("neg") or {}).items()},
                live2d=s.get("live2d"),
            )
        )
    return Rig(str(cfg.get("name", "rig")), sliders, canonical)


def load_rig(path: str | Path | None, canonical: Canonical) -> Rig:
    """读取绑定配置；path 为 None 时返回单位绑定。"""
    if path is None:
        return identity_rig(canonical)
    cfg = load_yaml(resolve_path(path))
    return rig_from_dict(cfg, canonical)


def identity_rig(canonical: Canonical) -> Rig:
    """单位绑定：每维一根 [0,1] 滑杆，key 就是表情基名（DESIGN §8 末句）。"""
    sliders = [Slider(key=n, zh=n, min=0.0, max=1.0, default=0.0, pos={n: 1.0}) for n in canonical.names]
    return Rig("identity", sliders, canonical, identity=True)
