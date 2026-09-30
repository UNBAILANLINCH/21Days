"""FACS 情绪原型合成器与「认不出」负样本（DESIGN §3.1、§3.3）。

正样本：按 configs/facs.yaml 里的情绪变体抽 AU 组合 → 按 A–E 强度档采样 → AU→表情基映射（同一基取最大）
       → 低强度干扰 AU → 随机眼球朝向（屏蔽维）→ 左右不对称抖动 → 高斯噪声 → 裁剪到 [0,1]。
       其中 clean_fraction 比例的「干净稀疏样本」跳过干扰 AU、眼球朝向、抖动与噪声，只保留核心 AU。
负样本：随机滑杆组合（随机稀疏度）+ 拮抗表情基同时拉满，去掉与任一类正样本最近邻过近的。

类别集（configs/label_sets/）：一个输出类可以由几个标注类组成（class_members），样本由各成员类平均分摊
（每类总数不变，成员各出一半 / 三分之一……余数给排在前面的成员）；集外类不在任何输出类里，不进正样本。
按绑定剔除做不出来的变体：variant_residuals 逐个变体（带 choose 的逐个备选）按统一强度摆出核心 AU、投影到绑定，
算相对残差；plan_variant_filter 按阈值定剔除清单，交给合成器的 exclude 参数。
"""

from __future__ import annotations

import dataclasses
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .canonical import Canonical
from .common import CONFIG_DIR, load_yaml, resolve_path

LEVELS = "ABCDE"
VALID_SOURCES = {"emfacs", "ckplus_min", "ckplus_fig1", "none"}

_GAZE_DIRECTIONS = [  # 屏蔽维，只为让屏蔽前的分布像真人
    ["eyeLookDownLeft", "eyeLookDownRight"],
    ["eyeLookUpLeft", "eyeLookUpRight"],
    ["eyeLookOutLeft", "eyeLookInRight"],
    ["eyeLookInLeft", "eyeLookOutRight"],
]


def parse_intensity(spec: str, default: str) -> tuple[int, int]:
    """"B" → (1,1)；"B-E" → (1,4)；"*" → default。返回档位下标闭区间。"""
    spec = str(spec).strip()
    if spec == "*":
        spec = default
    if "-" in spec:
        a, b = spec.split("-")
        lo, hi = LEVELS.index(a.strip()), LEVELS.index(b.strip())
    else:
        lo = hi = LEVELS.index(spec)
    if lo > hi:
        raise ValueError(f"强度档写反了：{spec}")
    return lo, hi


@dataclass
class _Variant:
    source: str
    aus: dict[str, str]
    choose: list[dict[str, str]]
    optional: dict[str, tuple[float, str]]
    unilateral: dict[str, str]
    drop_one_of: list[str]
    drop_prob: float
    weight: float
    note: str = ""


class FacsSynthesizer:
    """class_keys：输出类（顺序即 y 的下标）。
    class_members：{输出类: [facs.yaml 里的情绪 key]}，缺省时每类就是它自己（默认七类）。
    exclude：要剔除的变体，[{emotion, variant, choose}]，variant 是 facs.yaml 里的原始下标，
             choose 为 None 表示整条变体，否则只剔除那一个备选（剩下的备选平分，变体权重按保留比例缩小）。
    """

    def __init__(self, canonical: Canonical, class_keys: list[str], cfg: dict | None = None, cfg_path: str | Path | None = None,
                 class_members: dict[str, list[str]] | None = None, exclude: list[dict] | None = None):
        if cfg is None:
            cfg = load_yaml(resolve_path(cfg_path or CONFIG_DIR / "facs.yaml"))
        self.cfg = cfg
        self.canonical = canonical
        self.class_keys = list(class_keys)
        self.D = canonical.dim
        self.levels = np.array([cfg["intensity_levels"][l] for l in LEVELS], dtype=np.float64)  # [5,2]
        self.default_intensity = cfg.get("default_intensity", "B-E")
        syn = cfg.get("synthesis", {})
        self.combine = syn.get("combine", "max")
        self.asym_std = float(syn.get("asymmetry_std", 0.1))
        self.noise_std = float(syn.get("noise_std", 0.02))
        self.clean_fraction = float(syn.get("clean_fraction", 0.0))
        if not 0.0 <= self.clean_fraction <= 1.0:
            raise ValueError("synthesis.clean_fraction 必须在 [0,1] 内")
        dis = syn.get("distractor", {})
        self.distractor_probs = np.array(dis.get("count_probs", [1.0]), dtype=np.float64)
        self.distractor_probs /= self.distractor_probs.sum()
        self.distractor_intensity = dis.get("intensity", "A")
        self.distractor_aus = list(dis.get("aus", []))
        gaze = syn.get("gaze", {})
        self.gaze_prob = float(gaze.get("prob", 0.0))
        self.gaze_intensity = gaze.get("intensity", "A-C")

        # AU → 表情基向量
        self.au_vec: dict[str, np.ndarray] = {}
        for au, mapping in cfg["au_to_blendshape"].items():
            v = np.zeros(self.D)
            for n, w in mapping.items():
                v[canonical.index(n)] = w
            self.au_vec[au] = v
        self.side_L = np.array([s == "L" for s in canonical.sides])
        self.side_R = np.array([s == "R" for s in canonical.sides])
        self.paired = np.array([s != "C" for s in canonical.sides])

        # 输出类 → 情绪（标注类）；默认每类就是它自己
        members = class_members or {}
        self.class_members: dict[str, list[str]] = {k: list(members.get(k) or [k]) for k in self.class_keys}

        # 情绪变体（按情绪 key 存；默认七类时情绪 key 就是输出类 key）
        self.variants: dict[str, list[_Variant]] = {}
        for key in [e for k in self.class_keys for e in self.class_members[k]]:
            if key in self.variants:
                raise ValueError(f"情绪「{key}」被分进了不止一个输出类")
            if key not in cfg["emotions"]:
                raise ValueError(f"facs.yaml 里没有类别「{key}」的原型；labels.yaml 启用的每个类别都要有")
            vs = []
            for v in cfg["emotions"][key]["variants"]:
                src = v.get("source")
                if src not in VALID_SOURCES:
                    raise ValueError(f"{key} 的变体缺少合法 source（{sorted(VALID_SOURCES)}）：{v}")
                var = _Variant(
                    source=src,
                    aus=dict(v.get("aus") or {}),
                    choose=[dict(c) for c in (v.get("choose") or [])],
                    optional={a: (float(p[0]), str(p[1])) for a, p in (v.get("optional") or {}).items()},
                    unilateral=dict(v.get("unilateral") or {}),
                    drop_one_of=list(v.get("drop_one_of") or []),
                    drop_prob=float(v.get("drop_prob", 0.0)),
                    weight=float(v.get("weight", 1.0)),
                    note=str(v.get("note") or ""),
                )
                for au in list(var.aus) + [a for c in var.choose for a in c] + list(var.optional):
                    if au not in self.au_vec:
                        raise ValueError(f"{key} 用到了未映射的 {au}")
                vs.append(var)
            self.variants[key] = vs
        if exclude:
            self._apply_exclusions(exclude)

    def _apply_exclusions(self, exclude: list[dict]) -> None:
        """按剔除清单删变体 / 备选。备选部分剔除时，剩下的备选仍等概率抽，变体权重乘保留比例
        （等价于「原分布里只留做得出来的那部分再归一」）。某个情绪的变体全被剔除时报错。"""
        by_var: dict[tuple[str, int], set] = {}
        for e in exclude:
            emo, vi = str(e["emotion"]), int(e["variant"])
            if emo not in self.variants or not 0 <= vi < len(self.variants[emo]):
                raise ValueError(f"剔除清单里的变体不存在：{emo} #{vi}")
            ci = e.get("choose")
            by_var.setdefault((emo, vi), set()).add(None if ci is None else int(ci))
        for emo, vs in self.variants.items():
            kept = []
            for vi, var in enumerate(vs):
                drops = by_var.get((emo, vi))
                if not drops:
                    kept.append(var)
                    continue
                if None in drops or not var.choose:
                    continue  # 整条变体剔除
                alts = [c for ci, c in enumerate(var.choose) if ci not in drops]
                if alts:
                    kept.append(dataclasses.replace(var, choose=alts, weight=var.weight * len(alts) / len(var.choose)))
            if not kept:
                raise ValueError(f"情绪「{emo}」的变体全部被剔除（在当前绑定下都做不出来）：请在类别集里把它列入 drop，或检查绑定")
            self.variants[emo] = kept

    # ------------------------------------------------------------ 采样零件
    def _sample_value(self, spec: str, rng: np.random.Generator) -> float:
        lo, hi = parse_intensity(spec, self.default_intensity)
        lvl = rng.integers(lo, hi + 1)
        a, b = self.levels[lvl]
        return float(rng.uniform(a, b))

    def _au_contrib(self, au: str, value: float, side: str | None) -> np.ndarray:
        v = self.au_vec[au] * value
        if side == "L":
            v = np.where(self.side_R, 0.0, v)
        elif side == "R":
            v = np.where(self.side_L, 0.0, v)
        return v

    def _combine(self, x: np.ndarray, contrib: np.ndarray) -> np.ndarray:
        return np.maximum(x, contrib) if self.combine == "max" else x + contrib

    def _finish(self, x: np.ndarray, rng: np.random.Generator) -> np.ndarray:
        """干扰 AU、眼球朝向、左右不对称、噪声、裁剪。"""
        n_dis = rng.choice(len(self.distractor_probs), p=self.distractor_probs)
        if n_dis and self.distractor_aus:
            for au in rng.choice(self.distractor_aus, size=min(n_dis, len(self.distractor_aus)), replace=False):
                x = self._combine(x, self._au_contrib(str(au), self._sample_value(self.distractor_intensity, rng), None))
        if rng.random() < self.gaze_prob:
            val = self._sample_value(self.gaze_intensity, rng)
            for n in _GAZE_DIRECTIONS[rng.integers(len(_GAZE_DIRECTIONS))]:
                x[self.canonical.index(n)] = max(x[self.canonical.index(n)], val)
        jitter = 1.0 + rng.normal(0.0, self.asym_std, size=self.D)
        x = np.where(self.paired, x * jitter, x)
        x = x + rng.normal(0.0, self.noise_std, size=self.D)
        return np.clip(x, 0.0, 1.0)

    def sample_variant(self, var: _Variant, rng: np.random.Generator) -> np.ndarray:
        aus = dict(var.aus)
        if var.choose:
            aus.update(var.choose[rng.integers(len(var.choose))])
        for au, (p, spec) in var.optional.items():
            if rng.random() < p:
                aus[au] = spec
        if var.drop_one_of and rng.random() < var.drop_prob:
            cands = [a for a in var.drop_one_of if a in aus]
            if cands and len(aus) > 1:
                aus.pop(cands[rng.integers(len(cands))])
        rand_side = "L" if rng.random() < 0.5 else "R"
        x = np.zeros(self.D)
        for au, spec in aus.items():
            side = var.unilateral.get(au)
            if side == "random":
                side = rand_side
            x = self._combine(x, self._au_contrib(au, self._sample_value(spec, rng), side))
        return x

    # ------------------------------------------------------------ 正样本
    def sample_class(self, key: str, n: int, rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray]:
        """输出类 key 的 n 个样本，返回 (X [n, D], clean [n] bool)。
        由几个情绪组成时各情绪平分 n（余数给排在前面的），按成员顺序拼接；只有一个成员时与原先完全相同。"""
        members = self.class_members[key]
        if len(members) == 1:
            return self._sample_emotion(members[0], n, rng)
        m = len(members)
        counts = [n // m + (1 if i < n % m else 0) for i in range(m)]
        parts = [self._sample_emotion(emo, c, rng) for emo, c in zip(members, counts)]
        return np.concatenate([p[0] for p in parts]), np.concatenate([p[1] for p in parts])

    def _sample_emotion(self, key: str, n: int, rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray]:
        """情绪 key 的 n 个样本。干净样本数 = round(n · clean_fraction)，位置随机。"""
        vs = self.variants[key]
        w = np.array([v.weight for v in vs], dtype=np.float64)
        w /= w.sum()
        clean = np.zeros(n, dtype=bool)
        clean[rng.permutation(n)[: int(round(n * self.clean_fraction))]] = True
        out = np.zeros((n, self.D), dtype=np.float32)
        for i in range(n):
            var = vs[rng.choice(len(vs), p=w)]
            x = self.sample_variant(var, rng)
            out[i] = np.clip(x, 0.0, 1.0) if clean[i] else self._finish(x, rng)
        return out, clean

    def generate(self, n_per_class: int, seed: int, return_clean: bool = False):
        """返回 (X [C*n, D] float32, y [C*n] int64)，按种子可复现；return_clean=True 时再返回干净样本标记。"""
        rng = np.random.default_rng(seed)
        xs, ys, cs = [], [], []
        for c, key in enumerate(self.class_keys):
            x, cl = self.sample_class(key, n_per_class, rng)
            xs.append(x)
            cs.append(cl)
            ys.append(np.full(n_per_class, c, dtype=np.int64))
        X, y = np.concatenate(xs), np.concatenate(ys)
        return (X, y, np.concatenate(cs)) if return_clean else (X, y)

    # ------------------------------------------------------------ 按绑定判变体做不做得出来
    def variant_residuals(self, rig, intensity: float = 0.6) -> list[dict]:
        """逐个变体（带 choose 的逐个备选）：核心 AU（aus + 该备选；optional 不加、drop_one_of 不删）
        按统一强度摆出 → 屏蔽维置零 → 投影到绑定 → 相对残差 ||x − x̂||² / ||x||²。
        单侧 AU 取左侧（random 也取左侧）。没有核心 AU 的变体（中性）不判，rel_residual 为 None。
        variant 是当前变体列表里的下标：要在未剔除的合成器上调用，下标才对得上 facs.yaml。"""
        mask = self.canonical.mask_vector
        rows = []
        for cls in self.class_keys:
            for emo in self.class_members[cls]:
                for vi, var in enumerate(self.variants[emo]):
                    alts = list(enumerate(var.choose)) if var.choose else [(None, {})]
                    for ci, alt in alts:
                        aus = dict(var.aus)
                        aus.update(alt)
                        row = {"class": cls, "emotion": emo, "variant": vi, "choose": ci, "source": var.source,
                               "note": var.note, "aus": list(aus), "rel_residual": None}
                        if aus:
                            x = np.zeros(self.D)
                            for au in aus:
                                side = var.unilateral.get(au)
                                x = self._combine(x, self._au_contrib(au, intensity, "L" if side == "random" else side))
                            x = np.clip(x, 0.0, 1.0) * mask
                            den = float((x**2).sum())
                            if den > 1e-12:
                                _, xh = rig.project(x[None], mask)
                                row["rel_residual"] = float(((x - xh[0].astype(np.float64)) ** 2).sum() / den)
                        rows.append(row)
        return rows

    # ------------------------------------------------------------ 负样本
    def negatives(self, n: int, seed: int, rig=None, reference: np.ndarray | None = None) -> np.ndarray:
        """「认不出」负样本 [n, D]。rig 不为 None 时随机部分用随机滑杆组合，否则在规范空间随机稀疏取值。

        reference：用来判「离正样本太近」的正样本集合；None 时现场按每类 reference_per_class 个合成。
        """
        ncfg = self.cfg["negatives"]
        rng = np.random.default_rng(seed)
        if reference is None:
            ref_n = int(ncfg.get("reference_per_class", 500))
            reference, _ = self.generate(ref_n, seed + 7919)
        active = self.canonical.active_indices
        ref = reference[:, active].astype(np.float32)
        min_dist = float(ncfg.get("min_distance_to_positive", 0.0))
        mix = ncfg.get("mix", {"random": 0.5, "antagonist": 0.5})
        p_rand = float(mix.get("random", 0.5)) / (float(mix.get("random", 0.5)) + float(mix.get("antagonist", 0.5)))
        kept: list[np.ndarray] = []
        total = 0
        for _ in range(50):
            if total >= n:
                break
            m = max(256, int((n - total) * 1.5))
            n_rand = int(rng.binomial(m, p_rand))
            cand = np.concatenate([self._random_negatives(n_rand, rng, rig), self._antagonist_negatives(m - n_rand, rng)])
            rng.shuffle(cand)
            if min_dist > 0:
                d = _nn_distance(cand[:, active], ref)
                cand = cand[d >= min_dist]
            kept.append(cand)
            total += len(cand)
        out = np.concatenate(kept)[:n]
        if len(out) < n:
            raise RuntimeError("负样本生成不够：min_distance_to_positive 可能设得太大")
        return out.astype(np.float32)

    def _random_negatives(self, n: int, rng: np.random.Generator, rig) -> np.ndarray:
        frac = self.cfg["negatives"]["random"].get("active_fraction", [0.15, 0.6])
        if n == 0:
            return np.zeros((0, self.D), dtype=np.float32)
        if rig is not None and not rig.identity:
            s = rig.random_sliders(n, rng, frac)
            x = rig.forward(s).astype(np.float64)
        else:
            f = rng.uniform(frac[0], frac[1], size=(n, 1))
            x = np.where(rng.random((n, self.D)) < f, rng.uniform(0, 1, size=(n, self.D)), 0.0)
        x = x + rng.normal(0.0, self.noise_std, size=x.shape)
        return np.clip(x, 0, 1).astype(np.float32)

    def _antagonist_negatives(self, n: int, rng: np.random.Generator) -> np.ndarray:
        acfg = self.cfg["negatives"]["antagonist"]
        pairs = acfg["pairs"]
        lo, hi = acfg.get("pairs_per_sample", [1, 3])
        elo, ehi = acfg.get("extra_random_dims", [0, 4])
        spec = acfg.get("intensity", "D-E")
        out = np.zeros((n, self.D), dtype=np.float64)
        for i in range(n):
            x = np.zeros(self.D)
            k = int(rng.integers(lo, min(hi, len(pairs)) + 1))
            for j in rng.choice(len(pairs), size=k, replace=False):
                for name in list(pairs[j]["a"]) + list(pairs[j]["b"]):
                    x[self.canonical.index(name)] = self._sample_value(spec, rng)
            e = int(rng.integers(elo, ehi + 1))
            if e:
                for d in rng.choice(self.D, size=e, replace=False):
                    x[d] = max(x[d], self._sample_value("A-C", rng))
            x = x + rng.normal(0.0, self.noise_std, size=self.D)
            out[i] = np.clip(x, 0, 1)
        return out.astype(np.float32)


def _nn_distance(a: np.ndarray, ref: np.ndarray, chunk: int = 2048) -> np.ndarray:
    """a 中每个样本到 ref 的最近邻 L2 距离。"""
    ref = ref.astype(np.float32)
    rn = (ref**2).sum(1)
    out = np.empty(len(a), dtype=np.float32)
    for s in range(0, len(a), chunk):
        x = a[s : s + chunk].astype(np.float32)
        d2 = (x**2).sum(1)[:, None] + rn[None, :] - 2 * x @ ref.T
        out[s : s + chunk] = np.sqrt(np.maximum(d2.min(1), 0))
    return out


def plan_variant_filter(synth: FacsSynthesizer, rig, threshold: float = 0.9, intensity: float = 0.6) -> dict:
    """按绑定定剔除清单：相对残差 ≥ threshold 的变体 / 备选不进训练目标（规格 §15.2）。
    synth 必须是未剔除的合成器（variant 下标对应 facs.yaml）。单位绑定下投影无损，不剔除任何变体。"""
    if not 0.0 < float(threshold) <= 1.0:
        raise ValueError(f"变体剔除阈值必须在 (0, 1] 内：{threshold}")
    rows = synth.variant_residuals(rig, intensity)
    dropped = [r for r in rows if r["rel_residual"] is not None and r["rel_residual"] >= threshold]
    info = {
        "enabled": True,
        "rel_residual_threshold": float(threshold),
        "probe_intensity": float(intensity),
        "rig": rig.name,
        "rig_identity": bool(rig.identity),
        "rig_hash": rig.config_hash(),
        "n_checked": sum(r["rel_residual"] is not None for r in rows),
        "dropped": dropped,
        "variants": rows,
    }
    if rig.identity:
        info["note"] = "当前是单位绑定（没有绑定配置）：投影无损，所有变体都做得出来，不剔除任何变体"
    return info


def variant_sources(cfg: dict, class_keys: list[str]) -> dict[str, dict[str, int]]:
    """统计每类各出处的变体数（报告用）。"""
    out = {}
    for k in class_keys:
        cnt: dict[str, int] = {}
        for v in cfg["emotions"][k]["variants"]:
            cnt[v["source"]] = cnt.get(v["source"], 0) + 1
        out[k] = cnt
    return out
