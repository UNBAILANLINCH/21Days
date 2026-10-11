"""生成开局操作说明的**临时**占位素材（正式素材到位后本目录连同产物一起删）。

载体：`uv run --with pillow python scripts/placeholder-ui/make_tutorial_art.py`，人工显式调用，不后台保活。
产物：`Assets/_Project/Art/Sprites/UI/Tutorial/ui_tutorial_controls.png`（1920x1080 操作说明大图）、
      `Assets/_Project/Art/Sprites/UI/Tutorial/ui_tutorial_hint.png`（透明底「按任意键继续」提示条）。
锚点：两个 PNG 的像素尺寸与命令行回显；不读 Unity、不改 .meta（导入设置由 Unity 侧负责）。
退场条件：美术交付正式教程图后删掉本脚本与两个 PNG，预制体 `Prefabs/UI/TutorialView.prefab` 换引用即可。

键位来源：`Assets/_Project/Data/Input/GameInput.inputactions`（改键后重跑本脚本）。
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "_Project" / "Art" / "Sprites" / "UI" / "Tutorial"
FONT_PATH = ROOT / "Assets" / "_Project" / "Art" / "Fonts" / "Font_NotoSansSC_Regular.otf"

WIDTH, HEIGHT = 1920, 1080
BG = (18, 16, 22, 255)
PANEL = (30, 27, 36, 255)
GOLD = (216, 178, 90, 255)
TEXT = (232, 228, 220, 255)
MUTED = (140, 134, 148, 255)
CHIP_BG = (52, 47, 60, 255)
CHIP_LINE = (120, 104, 70, 255)

# (键位, 说明)，三栏：探索与战斗 / 对话中 / 手柄
EXPLORE_ROWS = [
    (["W", "A", "S", "D"], "移动（方向键同效）"),
    (["E", "F"], "交互 · 对话 · 拾取"),
    (["J"], "普通攻击"),
    (["左Shift", "左Ctrl"], "潜行 / 走·跑"),
    (["G"], "伪装"),
    (["F"], "背后处决"),
    (["R", "V"], "照镜 / 自照"),
    (["B", "Tab"], "背包 / 任务·图鉴"),
    (["H", "P"], "沉浸模式 / 暂停"),
]

DIALOGUE_ROWS = [
    (["空格", "回车"], "推进 · 连点补全"),
    (["A"], "自动播放"),
    (["S"], "倍速"),
    (["左Ctrl"], "跳过"),
    (["H"], "历史记录"),
    (["1", "2", "3", "4"], "选择选项"),
]

GAMEPAD_ROWS = [
    (["左摇杆"], "移动"),
    (["A"], "交互 / 对话"),
    (["X"], "普通攻击"),
    (["Y"], "伪装"),
    (["LB"], "潜行"),
    (["RB"], "背包"),
    (["LT", "RT"], "照镜 / 自照"),
    (["Start"], "暂停"),
]


def font(size):
    return ImageFont.truetype(str(FONT_PATH), size)


def draw_chip(draw, x, y, label, key_font):
    """画一个键位胶囊，返回它的宽度。"""
    pad_x, pad_y = 16, 10
    box = draw.textbbox((0, 0), label, font=key_font)
    width = box[2] - box[0] + pad_x * 2
    height = box[3] - box[1] + pad_y * 2
    draw.rounded_rectangle((x, y, x + width, y + height), radius=8, fill=CHIP_BG, outline=CHIP_LINE, width=2)
    draw.text((x + pad_x - box[0], y + pad_y - box[1]), label, font=key_font, fill=GOLD)
    return width


def draw_rows(draw, rows, x, y, width, key_font, label_font, row_height):
    """一行 = 左侧键位胶囊 + 右侧说明；返回画完后的 y。"""
    for keys, label in rows:
        cursor = x
        for key in keys:
            cursor += draw_chip(draw, cursor, y, key, key_font) + 10
        draw.text((x + width - 20, y + 8), label, font=label_font, fill=TEXT, anchor="ra")
        y += row_height
    return y


def draw_section(draw, title, x, y, width, title_font):
    draw.text((x, y), title, font=title_font, fill=GOLD)
    draw.line((x, y + 52, x + width, y + 52), fill=(90, 78, 52, 255), width=2)
    return y + 70


def build_controls():
    image = Image.new("RGBA", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(image)

    draw.rectangle((40, 40, WIDTH - 40, HEIGHT - 40), outline=(70, 62, 52, 255), width=3)
    draw.text((WIDTH // 2, 104), "操 作 说 明", font=font(76), fill=GOLD, anchor="ma")
    draw.text((WIDTH // 2, 204), "临时占位素材 · 待美术替换", font=font(28), fill=MUTED, anchor="ma")

    title_font, key_font, label_font = font(38), font(26), font(28)
    # 三栏：探索与战斗 / 对话中 / 手柄。行高与起始 y 一起把内容压进 y <= 920：
    # 底部留 160px 净空给 TutorialView 的「按任意键继续」提示条（它是独立精灵，否则会盖住最后两行说明）。
    columns = ((110, 520, "探索与战斗", EXPLORE_ROWS),
               (700, 520, "对话中", DIALOGUE_ROWS),
               (1290, 510, "手柄", GAMEPAD_ROWS))
    row_height = 62
    for x, width, title, rows in columns:
        y = draw_section(draw, title, x, 282, width, title_font)
        draw_rows(draw, rows, x, y, width, key_font, label_font, row_height)

    return image


def build_hint():
    """透明底的提示条，3 秒闸门打开后才由 TutorialView 淡入。

    底色不透明：它是叠在教程图上的，半透明会让底下的按键说明透出来、读起来脏。
    """
    image = Image.new("RGBA", (1200, 140), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 10, 1199, 129), radius=24, fill=(14, 12, 18, 255), outline=(120, 104, 70, 255), width=3)
    draw.text((600, 70), "按任意键继续", font=font(56), fill=GOLD, anchor="mm")
    return image


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, image in (("ui_tutorial_controls.png", build_controls()), ("ui_tutorial_hint.png", build_hint())):
        path = OUT_DIR / name
        image.save(path)
        print(f"{path.relative_to(ROOT)} {image.width}x{image.height}")


if __name__ == "__main__":
    main()
