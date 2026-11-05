# -*- coding: utf-8 -*-
"""生成 ComfyCollage 应用图标（多尺寸 ico + 预览图）。

图形含义：深色圆角底上是三张并排的图片卡片，代表“多模型横向对比”；
每张卡片的彩色标题条对应一个模型，与程序里生成对比图的配色一致。
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
CANVAS = 1024

BG = (31, 39, 51)
CARD_BODY = (245, 245, 245)
CARD_BORDER = (214, 214, 214)
PHOTO = (208, 208, 208)
COLORS = [
    (208, 90, 60),
    (40, 120, 200),
    (60, 160, 110),
]


def build_icon() -> Image.Image:
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # 圆角深色底
    draw.rounded_rectangle((0, 0, CANVAS - 1, CANVAS - 1), radius=210, fill=BG)

    # 三列卡片
    card_w = 232
    card_h = 470
    gap = 52
    total_w = card_w * 3 + gap * 2
    x0 = (CANVAS - total_w) // 2
    y0 = (CANVAS - card_h) // 2
    radius = 44
    bar_h = 92

    for index, color in enumerate(COLORS):
        x = x0 + index * (card_w + gap)
        right = x + card_w
        bottom = y0 + card_h

        # 卡片主体
        draw.rounded_rectangle((x, y0, right, bottom), radius=radius,
                               fill=CARD_BODY, outline=CARD_BORDER, width=4)

        # 顶部彩色标题条（只保留上半圆角）
        draw.rounded_rectangle((x, y0, right, y0 + bar_h), radius=radius, fill=color)
        draw.rectangle((x, y0 + bar_h - radius, right, y0 + bar_h), fill=color)

        # 极简“照片”符号：太阳 + 山
        cx = x + card_w // 2
        top = y0 + bar_h
        draw.ellipse((cx - 46, top + 72, cx - 46 + 54, top + 72 + 54), fill=PHOTO)
        base_y = bottom - 96
        draw.polygon(
            [(x + 34, base_y), (cx + 6, top + 168), (right - 30, base_y)],
            fill=PHOTO,
        )

    return image


def main():
    icon = build_icon()
    icon.save(ROOT / "icon_preview.png")
    icon.save(
        ROOT / "icon.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32),
               (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("已生成 icon.ico / icon_preview.png")


if __name__ == "__main__":
    main()
