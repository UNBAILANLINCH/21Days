# 现有 Unity 预览依赖编辑器；连接超时时，复用顾客拆件的正弦步态，用已有 Pillow 离线烘焙本角色。
# 载体：手动运行本脚本；锚点：输出帧数与断言；退场：Unity 可用后可改由真实剪辑采样。
# shortcut: 仅标定这八张赵十四拆件，重画部件或需要屈肘屈膝时重新制作关节。
import argparse
import hashlib
import json
import math
import shutil
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SIZE = (512, 768)
SCALE = 0.58
GROUND = 752
STATES = {"idle": 48, "walk": 24, "run": 16}
FILES = ["赵十四腿一.png", "赵十四腿二.png", "赵十四身体.png", "赵十四脖子.png",
         "赵十四胳膊二.png", "赵十四的脸部.png", "赵十四头部.png", "赵十四胳膊一.png"]
IDENTITY = (1, 0, 0, 0, 1, 0)


def multiply(a, b):
    x, y, z, u, v, w = a
    p, q, r, s, t, k = b
    return (x*p+y*s, x*q+y*t, x*r+y*k+z,
            u*p+v*s, u*q+v*t, u*r+v*k+w)


def joint(pivot, degrees=0, dx=0, dy=0):
    x, y = pivot
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
    return (c, -s, x+dx-c*x+s*y, s, c, y+dy-s*x-c*y)


def transformed(part, matrix):
    a, b, c, d, e, f = matrix
    det = a*e-b*d
    inverse = (e/det, -b/det, (b*f-e*c)/det,
               -d/det, a/det, (d*c-a*f)/det)
    # 预乘只用于重采样，输出转回直通 alpha，防止透明黑边。
    return part.convert("RGBa").transform(SIZE, Image.Transform.AFFINE, inverse,
                                          Image.Resampling.BICUBIC).convert("RGBA")


def render(parts, state, phase):
    phase %= 1
    wave = math.sin(phase*math.tau)
    moving, running = state != "idle", state == "run"
    # 步态沿用 animate-customer-parts 的交替正弦、半波平方抬脚、双频身体起伏。
    bob = ((12 if running else 6) if moving else 2.5)
    body = joint((346, 830), (-3.5 if running else 0)+(0.6 if moving else 0.2)*wave,
                 dy=-bob*(1-math.cos(phase*math.tau*(2 if moving else 1)))/2)
    head = joint((345, 568), (0.6 if moving else 0.3)*wave)
    canvas = Image.new("RGBA", SIZE)
    to_canvas = (SCALE, 0, 256-320*SCALE, 0, SCALE, GROUND-1278*SCALE)
    for index, part in enumerate(parts):
        if index < 2:
            sign = 1 if index == 0 else -1
            lift = ((42 if running else 22)*max(0, sign*wave)**2) if moving else 0
            matrix = joint(((295, 1120) if index == 0 else (390, 1125)),
                           sign*(10 if running else 5)*wave if moving else 0,
                           dx=sign*(30 if running else 17)*math.cos(phase*math.tau) if moving else 0)
            layer = transformed(part, multiply(to_canvas, matrix))
            # 保留原画的前后脚高度关系，每只支撑脚有固定落地点。
            bottom = layer.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()[3]
            target = round(GROUND-(1278-(1244 if index == 0 else 1278)+lift)*SCALE)
            matrix = list(multiply(to_canvas, matrix))
            matrix[5] += target-bottom
            layer = transformed(part, matrix)
        else:
            local = head if index in (5, 6) else IDENTITY
            if index in (4, 7):
                local = joint((300, 620) if index == 4 else (430, 635),
                              (0.65 if running else 0.35)*wave*(1 if index == 4 else -1))
            layer = transformed(part, multiply(to_canvas, multiply(body, local)))
        canvas = Image.alpha_composite(canvas, layer)
    return canvas


def preview(folder, destination):
    font = ImageFont.truetype("msyh.ttc", 18)
    frames = []
    for frame in range(96):
        canvas = Image.new("RGB", (768, 424), (224, 216, 197))
        draw = ImageDraw.Draw(canvas)
        for column, (state, count) in enumerate(STATES.items()):
            x = column*256
            draw.rounded_rectangle((x+4, 4, x+252, 420), 10, fill=(240, 234, 220))
            draw.text((x+18, 14), {"idle": "待机", "walk": "走路", "run": "奔跑"}[state],
                      font=font, fill=(52, 48, 42))
            draw.line((x+8, 408, x+248, 408), fill=(190, 179, 158))
            with Image.open(folder/f"chr_zhao_shisi_{state}_{frame % count+1:02d}.png") as source:
                small = source.resize((256, 384), Image.Resampling.LANCZOS)
                canvas.paste(small, (x, 32), small)
        frames.append(canvas)
    destination.mkdir(parents=True, exist_ok=True)
    frames[0].save(destination/"animations.gif", save_all=True, append_images=frames[1:],
                   duration=[42, 42, 41]*32, loop=0, disposal=2)
    contact = Image.new("RGB", (1024, 612), (240, 234, 220))
    draw = ImageDraw.Draw(contact)
    for row, (state, count) in enumerate(STATES.items()):
        for col in range(8):
            index = col*count//8+1
            with Image.open(folder/f"chr_zhao_shisi_{state}_{index:02d}.png") as source:
                small = source.resize((128, 192), Image.Resampling.LANCZOS)
                contact.paste(small, (col*128, row*204+12), small)
            draw.text((col*128+4, row*204), f"{state} {index:02d}", fill=(52, 48, 42))
    contact.save(destination/"contact.png")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    output = ROOT/"Assets/_Project/Art/Sprites/Characters/zhao_shisi"
    if output.exists():
        raise FileExistsError("目标已存在，拒绝覆盖："+str(output))
    source_files = [args.source/name for name in FILES]
    assert all(p.is_file() for p in source_files+[args.source/"赵十四.png"])
    parts = []
    for path in source_files:
        with Image.open(path) as image:
            assert image.size == (735, 1378) and image.mode == "RGBA", path.name
            assert image.getchannel("A").getextrema() == (0, 255), path.name
            parts.append(image.copy())
    combined = Image.new("RGBA", (735, 1378))
    for part in parts:
        combined = Image.alpha_composite(combined, part)
    with Image.open(args.source/"赵十四.png") as reference:
        white = Image.new("RGBA", reference.size, "white")
        diff = ImageChops.difference(Image.alpha_composite(white, combined).convert("RGB"),
                                    Image.alpha_composite(white, reference).convert("RGB"))
        # 源图层有重叠抗锯齿，记录重组误差供检查，不改原画补缝。
        reconstruction = sum(max(pixel) > 10 for pixel in diff.getdata())
    (output/"parts").mkdir(parents=True)
    manifest = {"source": "赵十四角色拆分素材", "width": 735, "height": 1378, "layers": []}
    for order, path in enumerate(source_files):
        target = output/"parts"/path.name
        shutil.copyfile(path, target)
        assert hashlib.sha256(path.read_bytes()).digest() == hashlib.sha256(target.read_bytes()).digest()
        manifest["layers"].append({"file": "parts/"+path.name, "sortingOrder": order})
    shutil.copyfile(args.source/"赵十四.png", output/"composite.png")
    (output/"parts.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    meta = {"frameWidth": SIZE[0], "frameHeight": SIZE[1], "fps": 24,
            "pivot": {"x": 0.5, "y": (SIZE[1]-GROUND)/SIZE[1]},
            "animations": {"walk": {"groundSpeed": 3}, "run": {"groundSpeed": 5}}}
    (output/"meta.json").write_text(json.dumps(meta, indent=2)+"\n", encoding="utf-8")
    for state, count in STATES.items():
        seam = ImageChops.difference(render(parts, state, 0), render(parts, state, 1))
        assert all(channel.getbbox() is None for channel in seam.split())
        hashes = set()
        for frame in range(count):
            image = render(parts, state, frame/count)
            bounds = image.getchannel("A").getbbox()
            assert bounds and bounds[0] > 4 and bounds[1] > 4 and bounds[2] < SIZE[0]-4 and bounds[3] < SIZE[1]-4
            assert image.getchannel("A").getextrema() == (0, 255)
            path = output/f"chr_zhao_shisi_{state}_{frame+1:02d}.png"
            image.save(path)
            hashes.add(hashlib.sha256(image.tobytes()).digest())
        assert len(hashes) >= count//2, (state, len(hashes))
        print(f"PASS {state}: {count} frames, {len(hashes)} unique, {SIZE}, straight RGBA, closed cycle")
    preview(output, ROOT/"Tools/zhao-shisi-preview")
    print(f"Source reconstruction: {reconstruction} pixels differ by >10/255")
    print(f"PASS: {sum(STATES.values())} frames; 9 originals preserved; previews in Tools/zhao-shisi-preview")


if __name__ == "__main__":
    main()
