# -*- coding: utf-8 -*-
"""核对 render_frames.py 的输出，并为每个角色生成一张 contact sheet。

检查：PNG 尺寸与 meta 一致、带 alpha 且非全透明、每状态首末帧非空、帧数与 meta 一致、
画布最外圈（边距内）没有不透明像素（有则说明被裁切）。
contact sheet：每个状态一行、最多 12 帧（均匀抽样），灰底，红线标出骨骼原点所在高度。
用法：python check_frames.py <帧输出根目录> [--sheets <目录>] [--sheet-scale 0.5]
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw


def check_char(d: Path, sheets: Path | None, sheet_scale: float) -> list[str]:
    errs: list[str] = []
    meta = json.loads((d / "meta.json").read_text(encoding="utf-8"))
    name, w, h = meta["name"], meta["frameWidth"], meta["frameHeight"]
    py = meta["pivotPx"]["y"]
    rows = []
    for st, info in meta["animations"].items():
        files = sorted(d.glob(f"chr_{name}_{st}_*.png"))
        if len(files) != info["frames"]:
            errs.append(f"{name}/{st}: 文件数 {len(files)} ≠ meta {info['frames']}")
        imgs = []
        for i, f in enumerate(files):
            im = Image.open(f)
            if im.size != (w, h):
                errs.append(f"{f.name}: 尺寸 {im.size} ≠ ({w},{h})")
            if im.mode != "RGBA":
                errs.append(f"{f.name}: 模式 {im.mode}，缺 alpha")
                continue
            a = im.getchannel("A")
            if a.getbbox() is None:
                if i in (0, len(files) - 1):
                    errs.append(f"{f.name}: 首/末帧全透明")
                else:
                    errs.append(f"{f.name}: 全透明")
            # 最外 1 px 圈应当全透明，否则就是出画布
            edge = [a.crop(b).getextrema()[1] for b in ((0, 0, w, 1), (0, h - 1, w, h), (0, 0, 1, h), (w - 1, 0, w, h))]
            if max(edge) > 0:
                errs.append(f"{f.name}: 画布边缘有不透明像素（疑似裁切）")
            imgs.append(im)
        n = len(imgs)
        pick = imgs if n <= 12 else [imgs[round(k * (n - 1) / 11)] for k in range(12)]
        rows.append((st, pick))
    if sheets:
        sheets.mkdir(parents=True, exist_ok=True)
        cw, ch = max(1, round(w * sheet_scale)), max(1, round(h * sheet_scale))
        label_w = 90
        sheet = Image.new("RGBA", (label_w + cw * 12, ch * len(rows)), (150, 150, 150, 255))
        dr = ImageDraw.Draw(sheet)
        for r, (st, pick) in enumerate(rows):
            y0 = r * ch
            dr.text((4, y0 + 4), f"{st}\n{len(pick)}/{meta['animations'][st]['frames']}", fill=(0, 0, 0, 255))
            for c, im in enumerate(pick):
                x0 = label_w + c * cw
                dr.rectangle((x0, y0, x0 + cw - 1, y0 + ch - 1), outline=(110, 110, 110, 255))
                sheet.alpha_composite(im.resize((cw, ch), Image.LANCZOS), (x0, y0))
            base = y0 + ch - round(py * sheet_scale)  # 骨骼原点高度（图像坐标自上而下）
            dr.line((label_w, base, sheet.width, base), fill=(255, 0, 0, 255), width=1)
        sheet.convert("RGB").save(sheets / f"{name}.png")
    return errs


def main():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser()
    ap.add_argument("root", type=Path)
    ap.add_argument("--sheets", type=Path)
    ap.add_argument("--sheet-scale", type=float, default=0.4)
    a = ap.parse_args()
    total_files, total_bytes, all_errs = 0, 0, []
    for d in sorted(p for p in a.root.iterdir() if (p / "meta.json").is_file()):
        errs = check_char(d, a.sheets, a.sheet_scale)
        pngs = list(d.glob("*.png"))
        size = sum(p.stat().st_size for p in pngs)
        total_files += len(pngs)
        total_bytes += size
        print(f"{d.name}: {len(pngs)} 张 PNG，{size / 1024 / 1024:.2f} MB，问题 {len(errs)}")
        for e in errs[:20]:
            print("   ", e)
        all_errs += errs
    print(f"合计 {total_files} 张 PNG，{total_bytes / 1024 / 1024:.2f} MB；问题 {len(all_errs)}")
    sys.exit(1 if all_errs else 0)


if __name__ == "__main__":
    main()
