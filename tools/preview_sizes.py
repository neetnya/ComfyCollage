# -*- coding: utf-8 -*-
"""生成小尺寸效果预览：把 16/24/32/48/64 放大并排，检查辨识度。"""
from pathlib import Path

from PIL import Image
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_icon import build_icon

ROOT = Path(__file__).resolve().parent.parent
sizes = [16, 24, 32, 48, 64]
icon = build_icon()

scale = 6
gap = 12
width = sum(s * scale for s in sizes) + gap * (len(sizes) + 1)
height = max(sizes) * scale + gap * 2
sheet = Image.new("RGBA", (width, height), (255, 255, 255, 255))

x = gap
for s in sizes:
    small = icon.resize((s, s), Image.Resampling.LANCZOS)
    big = small.resize((s * scale, s * scale), Image.Resampling.NEAREST)
    sheet.paste(big, (x, gap), big)
    x += s * scale + gap

sheet.save(ROOT / "icon_sizes.png")
print("已生成：icon_sizes.png")
