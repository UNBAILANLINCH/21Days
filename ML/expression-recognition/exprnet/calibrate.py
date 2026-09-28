"""校准与「认不出」判定（DESIGN §6）。

1. 温度缩放：在验证集上用 LBFGS 拟合 T，最小化 NLL（Guo 等 ICML 2017）；
2. 能量分数：energy = −T·logsumexp(logits / T)（Liu 等 NeurIPS 2020），越大越不像任何表情；
3. 阈值（TNR95）：标定用负样本（怪脸）95% 被拒时的 energy 作为 energy_threshold，即取其 energy 的第 5 百分位。
   标定集与留出负样本种子不同、样本不重叠；留出集只用来报拒识率，不参与定阈值。
   旧规则「分布内 95% 通过」（ID95）只算出来作对照：合成数据的分布内 energy 很集中，按它定的阈值会紧贴
   分布内一侧，把稍有偏离的正常脸误拒（DESIGN §6）。
"""

from __future__ import annotations

import numpy as np
import torch
import torch.nn.functional as F


def fit_temperature(logits: np.ndarray, labels: np.ndarray, max_iter: int = 200) -> float:
    """labels 为 [N] int 或 [N,C] 软标签。用 log T 参数化保证 T > 0。"""
    z = torch.as_tensor(logits, dtype=torch.float64)
    if labels.ndim == 1:
        target = F.one_hot(torch.as_tensor(labels, dtype=torch.long), z.shape[1]).double()
    else:
        target = torch.as_tensor(labels, dtype=torch.float64)
    log_t = torch.zeros(1, dtype=torch.float64, requires_grad=True)
    opt = torch.optim.LBFGS([log_t], lr=0.1, max_iter=max_iter, line_search_fn="strong_wolfe")

    def closure():
        opt.zero_grad()
        loss = -(target * F.log_softmax(z / log_t.exp(), dim=-1)).sum(-1).mean()
        loss.backward()
        return loss

    opt.step(closure)
    return float(np.clip(log_t.exp().item(), 0.05, 20.0))


def logsumexp_np(z: np.ndarray, axis: int = -1) -> np.ndarray:
    m = z.max(axis=axis, keepdims=True)
    return (m + np.log(np.exp(z - m).sum(axis=axis, keepdims=True))).squeeze(axis)


def energy_np(logits: np.ndarray, T: float) -> np.ndarray:
    return -T * logsumexp_np(logits / T)


def softmax_np(z: np.ndarray) -> np.ndarray:
    e = np.exp(z - z.max(-1, keepdims=True))
    return e / e.sum(-1, keepdims=True)


THRESHOLD_RULES = ("tnr95", "id95")


def threshold_ood_reject(energies_ood: np.ndarray, tnr: float = 0.95) -> float:
    """TNR 规则：负样本有 tnr 比例的 energy > 阈值（被拒），即取负样本 energy 的 (1 − tnr) 分位点。"""
    return float(np.quantile(energies_ood, 1.0 - tnr))


def threshold_id_pass(energies_in_dist: np.ndarray, tpr: float = 0.95) -> float:
    """旧规则（仅作对照）：分布内样本有 tpr 比例的 energy ≤ 阈值。"""
    return float(np.quantile(energies_in_dist, tpr))


def pick_threshold(rule: str, energies_in_dist: np.ndarray, energies_ood: np.ndarray, tnr: float = 0.95,
                   tpr: float = 0.95) -> tuple[float, dict]:
    """按规则取阈值，同时返回两种规则的阈值（供报告对照）。"""
    both = {"tnr95": threshold_ood_reject(energies_ood, tnr), "id95": threshold_id_pass(energies_in_dist, tpr)}
    if rule not in both:
        raise ValueError(f"未知阈值规则：{rule}（可选 {THRESHOLD_RULES}）")
    return both[rule], both


def decide(probs: np.ndarray, energy: np.ndarray, energy_thr: float, min_conf: float) -> np.ndarray:
    """游戏侧判定：返回类别下标，-1 表示「认不出」。"""
    pred = probs.argmax(-1)
    unknown = (energy > energy_thr) | (probs.max(-1) < min_conf)
    return np.where(unknown, -1, pred)
