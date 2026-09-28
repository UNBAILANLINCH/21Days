"""训练时的增强（DESIGN §3.2），全部在 torch 张量上按 batch 做。

顺序：选投影版 / 原值 → 强度缩放（默认只对公开集）→ 左右互换 → 屏蔽 → mixup（只对正样本）。
"""

from __future__ import annotations

import torch


def pick_projection(x_raw: torch.Tensor, x_proj: torch.Tensor, prob: float, gen: torch.Generator) -> torch.Tensor:
    """每个样本以 prob 取绑定投影版，否则取原值。"""
    if prob <= 0:
        return x_raw
    if prob >= 1:
        return x_proj
    r = torch.rand(x_raw.shape[0], 1, generator=gen) < prob
    return torch.where(r, x_proj, x_raw)


def intensity_scale(x: torch.Tensor, apply: torch.Tensor, prob: float, low: float, high: float,
                    gen: torch.Generator) -> torch.Tensor:
    """x' = clip(α·x, 0, 1)，α ~ U(low, high)；apply 为 [B] bool，只对这些行、再以 prob 概率生效。"""
    if prob <= 0 or not bool(apply.any()):
        return x
    B = x.shape[0]
    alpha = low + (high - low) * torch.rand(B, 1, generator=gen)
    use = (torch.rand(B, 1, generator=gen) < prob) & apply.view(B, 1)
    return torch.where(use, (x * alpha).clamp(0.0, 1.0), x)


def mirror(x: torch.Tensor, mirror_index: torch.Tensor, prob: float, gen: torch.Generator) -> torch.Tensor:
    """左右互换：L/R 表情基成对交换，标签不变。"""
    if prob <= 0:
        return x
    r = torch.rand(x.shape[0], 1, generator=gen) < prob
    return torch.where(r, x[:, mirror_index], x)


def mixup(x: torch.Tensor, y: torch.Tensor, alpha: float, gen: torch.Generator) -> tuple[torch.Tensor, torch.Tensor]:
    """batch 内随机配对混合，软标签按同一 λ 混合（λ ~ Beta(α, α)，取 max(λ, 1−λ) 让主成分占多数）。"""
    B = x.shape[0]
    lam = torch.distributions.Beta(alpha, alpha).sample((B, 1))
    lam = torch.maximum(lam, 1 - lam)
    perm = torch.randperm(B, generator=gen)
    return lam * x + (1 - lam) * x[perm], lam * y + (1 - lam) * y[perm]
