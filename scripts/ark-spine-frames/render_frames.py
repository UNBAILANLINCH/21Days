# -*- coding: utf-8 -*-
"""把 Spine 3.8 模型离线渲染成透明 PNG 序列帧（占位角色用）。

用法见同目录 README.md。依赖：Python 3.10+、playwright（pip），本机 Edge/Chrome；
vendor/spine-webgl.js 需自行下载（不提交，见 README 许可说明）。
"""
from __future__ import annotations

import argparse
import base64
import json
import math
import mimetypes
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

TOOL_DIR = Path(__file__).resolve().parent
HOST = "http://ark-spine.local"
STATE_MAP = {"Relax": "idle", "Move": "walk"}
BATCH = 8  # 每次 evaluate 取回的帧数，避免单次返回值过大


def state_name(anim: str) -> str:
    return STATE_MAP.get(anim, anim.lower())


def find_model(d: Path) -> tuple[Path, Path]:
    skels = sorted(d.glob("*.skel"))
    atlases = sorted(d.glob("*.atlas"))
    if not skels or not atlases:
        raise SystemExit(f"{d} 里缺 .skel 或 .atlas")
    return skels[0], atlases[0]


def parse_models(items: list[str]) -> list[tuple[str, Path]]:
    out = []
    for it in items:
        if "=" not in it:
            raise SystemExit(f"--model 需写成 名字=目录：{it}")
        name, d = it.split("=", 1)
        out.append((name.strip(), Path(d).expanduser().resolve()))
    return out


class Renderer:
    def __init__(self, channel: str, headful: bool):
        self.pw = sync_playwright().start()
        args = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"]
        try:
            self.browser = self.pw.chromium.launch(channel=channel or None, headless=not headful, args=args)
        except Exception as e:  # 本机没有该浏览器时退回 Playwright 自带 chromium
            print(f"[warn] 启动 {channel} 失败（{e.__class__.__name__}），改用自带 chromium", file=sys.stderr)
            self.browser = self.pw.chromium.launch(headless=not headful, args=args)
        self.page = None
        self.model_dir: Path | None = None

    def _route(self, route):
        url = route.request.url
        rel = url[len(HOST) + 1:].split("?")[0]
        if rel.startswith("model/"):
            path = self.model_dir / rel[len("model/"):]
        else:
            path = TOOL_DIR / rel
        if not path.is_file():
            route.fulfill(status=404, body=f"not found: {rel}")
            return
        ctype = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        route.fulfill(status=200, body=path.read_bytes(), headers={"Content-Type": ctype})

    def open_model(self, model_dir: Path, tex_pma: bool) -> dict:
        if self.page:
            self.page.close()
        self.model_dir = model_dir
        self.page = self.browser.new_page()
        self.page.on("console", lambda m: print(f"[page] {m.text}", file=sys.stderr)
                     if m.type in ("error", "warning") else None)
        self.page.route(f"{HOST}/**", self._route)
        self.page.goto(f"{HOST}/render.html")
        if not self.page.evaluate("window.spineReady === true"):
            raise SystemExit("spine-webgl.js 没加载上，检查 vendor/spine-webgl.js")
        skel, atlas = find_model(model_dir)
        return self.page.evaluate(
            "([s, a, p]) => window.loadModel(s, a, p)",
            [f"model/{skel.name}", f"model/{atlas.name}", tex_pma])

    def bounds(self, anims: list[str], fps: int) -> dict:
        return self.page.evaluate("([a, f]) => window.computeBounds(a, f)", [anims, fps])

    def setup_view(self, w, h, ox, oy, scale):
        self.page.evaluate("([w,h,x,y,s]) => window.setupView(w,h,x,y,s)", [w, h, ox, oy, scale])

    def frames(self, anim: str, fps: int):
        start, total = 0, None
        while total is None or start < total:
            r = self.page.evaluate("([a,f,s,c]) => window.renderFrames(a,f,s,c)", [anim, fps, start, BATCH])
            total = r["total"]
            for url in r["frames"]:
                yield base64.b64decode(url.split(",", 1)[1])
            start += len(r["frames"])
            if not r["frames"]:
                break

    def close(self):
        self.browser.close()
        self.pw.stop()


def layout(b: dict, margin: int, max_h: int) -> dict:
    """由包围盒并集算缩放、画布尺寸与原点像素位置（原点 x 居中）。"""
    raw_h = b["maxY"] - b["minY"]
    scale = 1.0
    if raw_h + 2 * margin > max_h:
        scale = (max_h - 2 * margin) / raw_h
    while True:  # 两端各自向上取整可能多出 1 px，缩一点直到画布高不超上限
        half_w = math.ceil(max(abs(b["minX"]), abs(b["maxX"])) * scale + margin)
        origin_y = math.ceil(-b["minY"] * scale + margin)
        top = math.ceil(b["maxY"] * scale + margin)
        if scale == 1.0 or origin_y + top <= max_h:
            break
        scale *= 0.999
    # 宽高补到 4 的倍数：DXT/ETC2/ASTC 4x4 块压缩要求，否则出包时这张图退回不压缩。
    # 宽两侧对称补（half_w 取偶数，原点仍居中），高只往顶上补（脚底原点像素不变）。
    half_w += half_w % 2
    top += (-(origin_y + top)) % 4
    return {"scale": scale, "width": 2 * half_w, "height": origin_y + top,
            "originX": half_w, "originY": origin_y, "rawHeight": raw_h}


def cmd_list(r: Renderer, models, tex_pma):
    for name, d in models:
        info = r.open_model(d, tex_pma)
        print(f"{name}: spine {info['version']}  skins={info['skins']}")
        for a in info["animations"]:
            print(f"    {a['name']:<16} {a['duration']:.4f}s -> {state_name(a['name'])}")


def cmd_pma_check(r: Renderer, models, out: Path, fps: int, margin: int, max_h: int):
    name, d = models[0]
    out.mkdir(parents=True, exist_ok=True)
    for tex_pma in (True, False):
        info = r.open_model(d, tex_pma)
        names = [a["name"] for a in info["animations"]]
        anim = "Relax" if "Relax" in names else names[0]
        lay = layout(r.bounds([anim], fps), margin, max_h)
        r.setup_view(lay["width"], lay["height"], lay["originX"], lay["originY"], lay["scale"])
        png = next(iter(r.frames(anim, fps)))
        flag = "true" if tex_pma else "false"
        p = out / f"{name}_{anim}_01_pma-{flag}.png"
        p.write_bytes(png)
        print(f"写出 {p}")


def select_anims(name: str, anims: list[dict], wanted: list[str] | None) -> list[dict]:
    """按 --anims 过滤（Spine 源动画名，区分大小写）；缺哪个直接报错，不静默跳过。"""
    if not wanted:
        return anims
    by_name = {a["name"]: a for a in anims}
    missing = [w for w in wanted if w not in by_name]
    if missing:
        raise SystemExit(f"{name}: 模型里没有动画 {missing}，现有 {sorted(by_name)}")
    return [by_name[w] for w in wanted]


def cmd_render(r: Renderer, models, out_root: Path, fps: int, margin: int, max_h: int, tex_pma: bool,
               wanted: list[str] | None = None):
    summary = {}
    for name, d in models:
        info = r.open_model(d, tex_pma)
        # 画布只按选中动画的包围盒并集算：只要 Relax/Move 时比全动画紧凑得多（Sit/Sleep 很占地方）
        anims = select_anims(name, info["animations"], wanted)
        b = r.bounds([a["name"] for a in anims], fps)
        lay = layout(b, margin, max_h)
        r.setup_view(lay["width"], lay["height"], lay["originX"], lay["originY"], lay["scale"])
        out = out_root / name
        out.mkdir(parents=True, exist_ok=True)
        for old in out.glob(f"chr_{name}_*.png"):
            old.unlink()
        meta_anims = {}
        for a in anims:
            st = state_name(a["name"])
            if st in meta_anims:
                raise SystemExit(f"{name}: 状态名冲突 {st}")
            n = 0
            for n, png in enumerate(r.frames(a["name"], fps), 1):
                (out / f"chr_{name}_{st}_{n:02d}.png").write_bytes(png)
            entry = {"source": a["name"], "frames": n, "duration": round(a["duration"], 4), "loop": True}
            if a["name"] not in STATE_MAP:
                entry["loopNote"] = "skel 不带循环标记，默认按循环处理"
            meta_anims[st] = entry
        meta = {
            "name": name, "fps": fps, "scale": round(lay["scale"], 6),
            "frameWidth": lay["width"], "frameHeight": lay["height"],
            "pivot": {"x": 0.5, "y": round(lay["originY"] / lay["height"], 6)},
            "pivotPx": {"x": lay["originX"], "y": lay["originY"]},
            "texturePremultiplied": tex_pma,
            "nativeBounds": {k: round(b[k], 3) for k in ("minX", "minY", "maxX", "maxY")},
            "animations": meta_anims,
        }
        (out / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        summary[name] = {"size": [lay["width"], lay["height"]], "rawHeight": round(lay["rawHeight"], 2),
                         "scale": lay["scale"], "frames": {k: v["frames"] for k, v in meta_anims.items()}}
        print(f"{name}: {lay['width']}x{lay['height']} 原生高 {lay['rawHeight']:.1f} scale {lay['scale']:.4f} "
              + " ".join(f"{k}={v['frames']}" for k, v in meta_anims.items()))
    return summary


def main():
    ap = argparse.ArgumentParser(description="Spine 3.8 模型 → 透明 PNG 序列帧")
    ap.add_argument("--model", action="append", required=True, help="名字=模型目录（含 .skel/.atlas/.png），可重复")
    ap.add_argument("--out", type=Path, help="输出根目录，每个角色一个子目录")
    ap.add_argument("--fps", type=int, default=24,
                    help="采样帧率；走 / 跑建议 24（12 时步态发顿），待机 24 或 12 皆可")
    ap.add_argument("--margin", type=int, default=4, help="包围盒外扩像素")
    ap.add_argument("--max-height", type=int, default=512, help="画布高超过此值时整体缩小")
    ap.add_argument("--anims", default="",
                    help="只渲这些 Spine 源动画，逗号分隔（如 Relax,Move）；默认全部。画布只按选中动画算")
    ap.add_argument("--tex-pma", choices=["true", "false"], default="true",
                    help="贴图是否已预乘 alpha（方舟基建小人实测结论见 README）")
    ap.add_argument("--channel", default="msedge", help="msedge / chrome / 空串=Playwright 自带 chromium")
    ap.add_argument("--headful", action="store_true")
    ap.add_argument("--list", action="store_true", help="只列动画名与时长")
    ap.add_argument("--pma-check", type=Path, help="只把第一个模型 Relax 第 1 帧按 pma=true/false 各渲一张到此目录")
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):  # Windows 控制台默认 GBK，中文输出会乱码
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass
    models = parse_models(a.model)
    tex_pma = a.tex_pma == "true"
    r = Renderer(a.channel, a.headful)
    try:
        if a.list:
            cmd_list(r, models, tex_pma)
        elif a.pma_check:
            cmd_pma_check(r, models, a.pma_check, a.fps, a.margin, a.max_height)
        else:
            if not a.out:
                raise SystemExit("需要 --out")
            wanted = [s.strip() for s in a.anims.split(",") if s.strip()] or None
            summary = cmd_render(r, models, a.out, a.fps, a.margin, a.max_height, tex_pma, wanted)
            (a.out / "_summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2),
                                                 encoding="utf-8")
    finally:
        r.close()


if __name__ == "__main__":
    main()
