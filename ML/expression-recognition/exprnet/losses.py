"""训练目标（DESIGN §5）。

- 主损失：软标签交叉熵 + 标签平滑
- 类别不平衡：logit adjustment，训练时用 logits + τ·log π（推理时用原始 logits）
- 认不出：Outlier Exposure，负样本推向均匀分布（在原始 logits 上算，因为推理时看的就是它）
- 可选：对称交叉熵 SCE（噪声标签）
"""

from __future__ import annotations

import math

import torch
import torch.nn.functional as F


def smooth_targets(target: torch.Tensor, smoothing: float) -> torch.Tensor:
    C = target.shape[-1]
    return target * (1.0 - smoothing) + smoothing / C


def soft_cross_entropy(logits: torch.Tensor, target: torch.Tensor, smoothing: float = 0.0,
                       log_prior: torch.Tensor | None = None, tau: float = 1.0) -> torch.Tensor:
    """target 为软标签 [B,C]（每行和为 1）。log_prior 不为 None 时做 logit adjustment。"""
    if log_prior is not None and tau != 0.0:
        logits = logits + tau * log_prior
    t = smooth_targets(target, smoothing) if smoothing > 0 else target
    return -(t * F.log_softmax(logits, dim=-1)).sum(-1).mean()


def outlier_exposure_loss(logits: torch.Tensor) -> torch.Tensor:
    """与均匀分布的交叉熵：−mean_c log softmax(z)_c。输出均匀时取最小值 log C。"""
    return -F.log_softmax(logits, dim=-1).mean(-1).mean()


def reverse_cross_entropy(logits: torch.Tensor, target: torch.Tensor, log_clamp: float = -4.0) -> torch.Tensor:
    """RCE = −Σ p·log q，q 为标签，log 0 截断到 log_clamp（Wang 等 ICCV 2019 取 A = −4）。"""
    p = F.softmax(logits, dim=-1)
    log_q = torch.clamp(torch.log(target.clamp_min(1e-12)), min=log_clamp)
    return -(p * log_q).sum(-1).mean()


class ExprLoss:
    def __init__(self, cfg: dict, class_prior: torch.Tensor | None):
        self.smoothing = float(cfg.get("label_smoothing", 0.1))
        self.tau = float(cfg.get("logit_adjust_tau", 1.0))
        self.oe_weight = float(cfg.get("oe_weight", 0.5))
        sce = cfg.get("sce", {}) or {}
        self.sce = bool(sce.get("enabled", False))
        self.sce_alpha = float(sce.get("alpha", 0.1))
        self.sce_beta = float(sce.get("beta", 1.0))
        self.log_prior = None if class_prior is None else torch.log(class_prior.clamp_min(1e-12))

    def __call__(self, logits_pos: torch.Tensor, target: torch.Tensor, logits_neg: torch.Tensor | None) -> tuple[torch.Tensor, dict]:
        ce = soft_cross_entropy(logits_pos, target, self.smoothing, self.log_prior, self.tau)
        if self.sce:
            adj = logits_pos + self.tau * self.log_prior if self.log_prior is not None else logits_pos
            main = self.sce_alpha * ce + self.sce_beta * reverse_cross_entropy(adj, target)
        else:
            main = ce
        parts = {"ce": float(ce.detach())}
        loss = main
        if logits_neg is not None and logits_neg.shape[0] > 0 and self.oe_weight > 0:
            oe = outlier_exposure_loss(logits_neg)
            # 记录的是超出下界 log C 的部分，便于看收敛
            parts["oe"] = float(oe.detach()) - math.log(logits_neg.shape[-1])
            loss = loss + self.oe_weight * oe
        return loss, parts
