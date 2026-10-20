"""将 Unity 导出的真实序列帧排成预览；不另算动作，不修改源拆件。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

source = Path("Assets/_Project/Art/Sprites/Characters/lunhuitang_guard")
output = Path("ai-docs/docs/lunhuitang-guard-20261019")
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("msyh.ttc", 18)
states = [("idle", "待机"), ("walk", "走路"), ("talk", "说话")]
frames = []
for index in range(48):
    canvas = Image.new("RGB", (768, 302), (230, 224, 208))
    draw = ImageDraw.Draw(canvas)
    for column, (state, label) in enumerate(states):
        count = 24 if state == "walk" else 48
        with Image.open(source / f"chr_lunhuitang_guard_{state}_{index % count + 1:02d}.png") as original:
            assert original.size == (512, 512) and original.mode == "RGBA"
            assert original.getpixel((0, 0))[3] == 0
            image = original.resize((256, 256), Image.Resampling.LANCZOS)
        canvas.paste(image, (column * 256, 40), image)
        draw.text((column * 256 + 100, 9), label, font=font, fill=(40, 48, 54))
    frames.append(canvas)
frames[0].save(output / "assembly.png")
frames[0].save(output / "motions.gif", save_all=True, append_images=frames[1:], duration=42, loop=0, disposal=2)
with Image.open(output / "motions.gif") as result:
    assert result.n_frames == 48 and result.size == (768, 302)
print(output / "motions.gif")
