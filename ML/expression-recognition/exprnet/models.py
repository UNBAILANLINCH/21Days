"""分类网络（DESIGN §4）：regionformer（默认）与 resmlp（基线）。

两者输入都是规范空间 [B, 51]（屏蔽维已置零），输出 logits [B, C]。
注意力手写（Linear 出 qkv → Reshape/Transpose → MatMul → Softmax），不用 nn.MultiheadAttention，
保证导出的 ONNX 算子都在 Sentis 支持清单里；reshape 只用常量形状（batch 维写 -1），不引入动态形状算子。
"""

from __future__ import annotations

import math

import torch
import torch.nn as nn
import torch.nn.functional as F

from .canonical import REGIONS, SIDES, Canonical


class Attention(nn.Module):
    def __init__(self, d_model: int, heads: int, n_tokens: int, dropout: float):
        super().__init__()
        if d_model % heads:
            raise ValueError("d_model 必须能被 heads 整除")
        self.h = heads
        self.dh = d_model // heads
        self.t = n_tokens
        self.d = d_model
        self.scale = 1.0 / math.sqrt(self.dh)
        self.qkv = nn.Linear(d_model, 3 * d_model)
        self.proj = nn.Linear(d_model, d_model)
        self.drop = nn.Dropout(dropout)

    def forward(self, x: torch.Tensor) -> torch.Tensor:  # [B, T, d]
        qkv = self.qkv(x).reshape(-1, self.t, 3, self.h, self.dh).permute(2, 0, 3, 1, 4)  # [3, B, h, T, dh]
        q, k, v = qkv[0], qkv[1], qkv[2]
        att = torch.matmul(q * self.scale, k.transpose(-2, -1))  # [B, h, T, T]
        att = self.drop(torch.softmax(att, dim=-1))
        out = torch.matmul(att, v).transpose(1, 2).reshape(-1, self.t, self.d)  # [B, T, d]
        return self.proj(out)


class Block(nn.Module):
    """pre-LN Transformer 块：x + Attn(LN(x))；x + FFN(LN(x))。"""

    def __init__(self, d_model: int, heads: int, n_tokens: int, ffn_mult: int, dropout: float):
        super().__init__()
        self.ln1 = nn.LayerNorm(d_model)
        self.attn = Attention(d_model, heads, n_tokens, dropout)
        self.ln2 = nn.LayerNorm(d_model)
        self.fc1 = nn.Linear(d_model, d_model * ffn_mult)
        self.fc2 = nn.Linear(d_model * ffn_mult, d_model)
        self.drop = nn.Dropout(dropout)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = x + self.drop(self.attn(self.ln1(x)))
        x = x + self.drop(self.fc2(self.drop(F.gelu(self.fc1(self.ln2(x))))))
        return x


class RegionFormer(nn.Module):
    """每个未屏蔽的表情基是一个 token（FT-Transformer 思路）：

        token_i = x_i · w_{g(i)} + b_i + 区域嵌入[region_i] + 侧嵌入[side_i]

    左右成对的两维共享 w（对称先验）；前面拼一个 CLS token，过 L 层 pre-LN Transformer，取 CLS 分类。
    被屏蔽的维不建 token（它们恒为 0，建了也只是常量）。
    """

    def __init__(self, canonical: Canonical, num_classes: int, d_model: int = 48, heads: int = 4, layers: int = 2,
                 ffn_mult: int = 2, dropout: float = 0.1):
        super().__init__()
        active = torch.as_tensor(canonical.active_indices, dtype=torch.long)
        groups_all = torch.as_tensor(canonical.pair_groups, dtype=torch.long)
        g_active = groups_all[active]
        # 分组编号重排成 0..G-1
        uniq, g_remap = torch.unique(g_active, return_inverse=True)
        self.register_buffer("active_idx", active, persistent=False)
        self.register_buffer("group_idx", g_remap, persistent=False)
        self.register_buffer("region_idx", torch.as_tensor(canonical.region_ids, dtype=torch.long)[active], persistent=False)
        self.register_buffer("side_idx", torch.as_tensor(canonical.side_ids, dtype=torch.long)[active], persistent=False)
        T = len(active)
        self.n_tokens = T
        self.w = nn.Parameter(torch.randn(len(uniq), d_model) * 0.5)
        self.b = nn.Parameter(torch.zeros(T, d_model))
        self.region_emb = nn.Parameter(torch.randn(len(REGIONS), d_model) * 0.02)
        self.side_emb = nn.Parameter(torch.randn(len(SIDES), d_model) * 0.02)
        self.cls = nn.Parameter(torch.zeros(1, 1, d_model))
        self.blocks = nn.ModuleList([Block(d_model, heads, T + 1, ffn_mult, dropout) for _ in range(layers)])
        self.ln = nn.LayerNorm(d_model)
        self.head = nn.Linear(d_model, num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:  # [B, D]
        xa = x[:, self.active_idx].unsqueeze(-1)  # [B, T, 1]
        # 这几项与输入无关，导出时会被常量折叠成一张 [T, d] 表
        w = self.w[self.group_idx]  # [T, d]
        bias = self.b + self.region_emb[self.region_idx] + self.side_emb[self.side_idx]
        tok = xa * w + bias  # [B, T, d]
        # 用「输入切片 × 0 + CLS 参数」得到 [B,1,d]：比 expand(B, …) 少一串 Shape/Gather/Where 等动态形状算子
        cls = tok[:, :1, :] * 0.0 + self.cls
        h = torch.cat([cls, tok], dim=1)
        for blk in self.blocks:
            h = blk(h)
        return self.head(self.ln(h[:, 0]))


class ResMLP(nn.Module):
    """基线：D → width → blocks 个残差块（LN-Linear-GELU-Dropout-Linear）→ LN → 分类头。"""

    def __init__(self, canonical: Canonical, num_classes: int, width: int = 128, hidden: int = 256, blocks: int = 2,
                 dropout: float = 0.1):
        super().__init__()
        self.inp = nn.Linear(canonical.dim, width)
        self.blocks = nn.ModuleList()
        for _ in range(blocks):
            self.blocks.append(nn.ModuleDict({
                "ln": nn.LayerNorm(width),
                "fc1": nn.Linear(width, hidden),
                "fc2": nn.Linear(hidden, width),
            }))
        self.drop = nn.Dropout(dropout)
        self.ln = nn.LayerNorm(width)
        self.head = nn.Linear(width, num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        h = self.inp(x)
        for blk in self.blocks:
            h = h + blk["fc2"](self.drop(F.gelu(blk["fc1"](blk["ln"](h)))))
        return self.head(self.ln(h))


def build_model(name: str, model_cfg: dict, canonical: Canonical, num_classes: int) -> nn.Module:
    kw = dict(model_cfg.get(name, {}) or {})
    if name == "regionformer":
        return RegionFormer(canonical, num_classes, **kw)
    if name == "resmlp":
        return ResMLP(canonical, num_classes, **kw)
    raise ValueError(f"未知模型：{name}（可选 regionformer / resmlp）")


def count_params(model: nn.Module) -> int:
    return sum(p.numel() for p in model.parameters() if p.requires_grad)


# ---------------------------------------------------------------- 读取训练产物


def load_checkpoint(run_dir, map_location: str = "cpu"):
    """读取 artifacts/runs/<名字>/ckpt.pt，返回 (classifier, ckpt 字典, canonical)。

    canonical 用本工程当前的 canonical.yaml，再按 ckpt 里记录的维度顺序与屏蔽维核对 / 覆盖。
    """
    from pathlib import Path

    from .canonical import load_canonical

    run_dir = Path(run_dir)
    ckpt = torch.load(run_dir / "ckpt.pt", map_location=map_location, weights_only=False)
    canonical = load_canonical(masked=ckpt["masked_names"])
    if canonical.names != ckpt["canonical_names"]:
        raise ValueError("训练产物的规范空间维度顺序与当前 canonical.yaml 不一致，不能直接加载")
    model = build_model(ckpt["model_name"], {ckpt["model_name"]: ckpt["model_cfg"]}, canonical, len(ckpt["class_keys"]))
    model.load_state_dict(ckpt["state_dict"])
    model.eval()
    return model, ckpt, canonical
