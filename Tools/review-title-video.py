# 只读取参考视频并生成检查帧，不改源视频或游戏资产。
import sys, json, math
from pathlib import Path
import imageio_ffmpeg
from PIL import Image, ImageDraw

source, destination = Path(sys.argv[1]), Path(sys.argv[2])
destination.mkdir(parents=True, exist_ok=True)
frames = imageio_ffmpeg.read_frames(str(source), pix_fmt="rgb24")
info = next(frames)
width, height = info["size"]
fps, duration = info["fps"], info["duration"]
indices = sorted(set(round(i * max(0, duration * fps - 2) / 19) for i in range(20)))
tiles = []
for index, data in enumerate(frames):
    if index not in indices:
        continue
    picture = Image.frombytes("RGB", (width, height), data)
    picture.save(destination / f"frame-{index:05d}.png")
    picture.thumbnail((360, 360))
    tile = Image.new("RGB", (360, 390), "#252525")
    tile.paste(picture, ((360 - picture.width) // 2, 30))
    ImageDraw.Draw(tile).text((10, 8), f"{index / fps:.3f} s", fill="white")
    tiles.append(tile)
    if index >= indices[-1]:
        break
frames.close()
sheet = Image.new("RGB", (360 * 4, 390 * math.ceil(len(tiles) / 4)), "#252525")
for index, tile in enumerate(tiles):
    sheet.paste(tile, ((index % 4) * 360, (index // 4) * 390))
sheet.save(destination / "contact-sheet.png")
print(json.dumps({"name": source.name, "width": width, "height": height, "fps": fps,
                  "duration": duration, "sampled": len(tiles), "sheet": str(destination / "contact-sheet.png")}, ensure_ascii=False))
