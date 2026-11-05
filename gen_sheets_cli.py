import os
import re
import sys
from pathlib import Path

# WPF 重定向标准输出时强制使用 UTF-8；-u 参数同时负责关闭缓冲。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True, write_through=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace", line_buffering=True, write_through=True)


from PIL import Image, ImageDraw, ImageFont, ImageOps

IMAGE_EXTENSIONS = {
    ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
}

TILE_SIZE = 500
GAP = 8
LABEL_HEIGHT = 30
HEADER_HEIGHT = 34
TOP_GAP = 8
FILES_PER_SHEET = 3
BACKGROUND = (245, 245, 245)
TILE_BACKGROUND = (232, 232, 232)


def natural_key(value):
    """让 2.png 排在 10.png 前面。"""
    return [
        int(part) if part.isdigit() else part.casefold()
        for part in re.split(r"(\d+)", value)
    ]


def load_fonts():
    try:
        return (
            ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 20),
            ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 15),
        )
    except OSError:
        default = ImageFont.load_default()
        return default, default


def fit_image(path):
    """保持原图比例，完整放入正方形画布，不拉伸、不裁切。"""
    with Image.open(path) as source:
        image = ImageOps.exif_transpose(source)
        image = image.convert("RGB")
        image.thumbnail((TILE_SIZE, TILE_SIZE), Image.Resampling.LANCZOS)

        canvas = Image.new("RGB", (TILE_SIZE, TILE_SIZE), TILE_BACKGROUND)
        x = (TILE_SIZE - image.width) // 2
        y = (TILE_SIZE - image.height) // 2
        canvas.paste(image, (x, y))
        return canvas


def main():
    if len(sys.argv) < 2:
        print("[ERROR] 未提供图片根目录。")
        return 1

    base = Path(sys.argv[1]).expanduser()
    if not base.is_dir():
        print(f"[ERROR] 目录不存在或不是有效目录：{base}")
        return 1

    models = sorted(
        [
            item for item in base.iterdir()
            if item.is_dir()
            and not item.name.startswith((".", "_"))
        ],
        key=lambda item: natural_key(item.name),
    )

    if not models:
        print("[ERROR] 根目录下没有找到模型文件夹。")
        return 1

    print(f"[INFO] 识别到 {len(models)} 个模型：{', '.join(m.name for m in models)}")

    # 只收集支持的图片格式，文件名仍按完整文件名匹配。
    indices = set()
    for model in models:
        for item in model.iterdir():
            if item.is_file() and item.suffix.casefold() in IMAGE_EXTENSIONS:
                indices.add(item.name)

    indices = sorted(indices, key=natural_key)
    if not indices:
        print("[ERROR] 模型文件夹中没有找到支持的图片文件。")
        return 1

    print(f"[INFO] 共识别到 {len(indices)} 个不同图片文件名。")

    output_dir = Path(__file__).resolve().parent / "_output"
    output_dir.mkdir(parents=True, exist_ok=True)
    print(f"[INFO] 输出目录：{output_dir}")

    palette = [
        (208, 90, 60), (40, 120, 200), (60, 160, 110),
        (150, 60, 150), (200, 150, 40), (100, 100, 100),
    ]
    colors = {
        model.name: palette[i % len(palette)]
        for i, model in enumerate(models)
    }

    title_font, _ = load_fonts()
    groups = [
        indices[i:i + FILES_PER_SHEET]
        for i in range(0, len(indices), FILES_PER_SHEET)
    ]

    missing_count = 0
    error_count = 0

    for group_index, group in enumerate(groups, start=1):
        print(f"[PROCESS] 正在生成第 {group_index}/{len(groups)} 张对比图……")

        width = len(models) * TILE_SIZE + (len(models) + 1) * GAP
        # 模型标题和第一行文件名使用独立区域，避免顶部文字重叠。
        height = HEADER_HEIGHT + LABEL_HEIGHT + len(group) * (TILE_SIZE + LABEL_HEIGHT) + GAP
        sheet = Image.new("RGB", (width, height), BACKGROUND)
        draw = ImageDraw.Draw(sheet)

        # 模型标题
        for column, model in enumerate(models):
            x = GAP + column * (TILE_SIZE + GAP)
            color = colors[model.name]
            draw.rectangle((x, 0, x + TILE_SIZE, 4), fill=color)
            draw.text((x + 6, 7), model.name, font=title_font, fill=color)

        # 每行对应一个文件名，每列对应一个模型
        for row, filename in enumerate(group):
            y = HEADER_HEIGHT + LABEL_HEIGHT + row * (TILE_SIZE + LABEL_HEIGHT)
            draw.text((GAP, y - LABEL_HEIGHT + 5), filename,
                      font=title_font, fill=(20, 20, 20))

            for column, model in enumerate(models):
                x = GAP + column * (TILE_SIZE + GAP)
                image_path = model / filename

                if image_path.is_file():
                    try:
                        tile = fit_image(image_path)
                        sheet.paste(tile, (x, y))
                    except Exception as exc:
                        error_count += 1
                        draw.rectangle(
                            (x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1),
                            fill=(255, 220, 220),
                        )
                        draw.text((x + 190, y + 235), "Error",
                                  font=title_font, fill=(180, 0, 0))
                        print(f"[WARN] 图片读取失败：{image_path}：{exc}")
                else:
                    missing_count += 1
                    draw.rectangle(
                        (x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1),
                        fill=(220, 220, 220),
                    )
                    draw.text((x + 180, y + 235), "Missing",
                              font=title_font, fill=(120, 120, 120))

                draw.rectangle(
                    (x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1),
                    outline=(190, 190, 190),
                )

        output_path = output_dir / f"sheet_{group_index:02d}.png"
        sheet.save(output_path, format="PNG", optimize=True)
        print(f"[SUCCESS] 已保存：{output_path}")

    print(
        f"[DONE] 全部完成。共生成 {len(groups)} 张对比图；"
        f"缺失图片 {missing_count} 张；读取失败 {error_count} 张。"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\n[ERROR] 用户中断任务。")
        sys.exit(130)
    except Exception as exc:
        print(f"[ERROR] 程序发生未处理异常：{exc}")
        sys.exit(1)
