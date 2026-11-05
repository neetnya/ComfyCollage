# ComfyCollage

ComfyCollage 是一个 Windows WPF 小工具，用于横向比较多个 AI 绘图模型生成的图片。

## 1. 工作方式

- WPF 负责选择图片根目录、启动 Python、显示实时日志。
- Python + Pillow 负责扫描图片、缩放排版和生成 PNG 拼图。
- 输出目录为所选图片根目录下的 `_output` 文件夹。

## 2. 环境要求

- Windows 10 / Windows 11
- .NET 8 Desktop Runtime（运行已编译程序时需要）
- Python 3.8 或更高版本，且 `python` 命令可以在终端中执行
- Python Pillow 库

安装 Pillow：

```powershell
python -m pip install --upgrade Pillow
```

检查环境：

```powershell
python --version
python -c "from PIL import Image; print('Pillow OK')"
```

## 3. 图片目录结构

选择的根目录应当直接包含各个模型文件夹：

```text
图片根目录/
├─ boleromix/
│  ├─ 001.png
│  ├─ 002.png
│  └─ 003.png
├─ wai17/
│  ├─ 001.png
│  ├─ 002.png
│  └─ 003.png
└─ zuki/
   ├─ 001.png
   ├─ 002.png
   └─ 003.png
```

每个模型文件夹代表一个模型。程序按完整文件名匹配图片：
- 相同文件名会排列在同一行，方便比较不同模型。
- 某个模型缺少该文件时，对应位置显示 `Missing`。
- 文件名区分扩展名，例如 `001.png` 和 `001.jpg` 会被视为两个不同条目。
- 根目录下以 `.` 或 `_` 开头的子文件夹会被忽略。

## 4. 支持的图片格式

`.png`、`.jpg`、`.jpeg`、`.webp`、`.bmp`、`.gif`、`.tif`、`.tiff`。

其他文件类型会被忽略。

## 5. 图片排版规则

- 模型文件夹按名称排序，列顺序保持稳定。
- 图片文件名采用自然排序，例如 `2.png` 会排在 `10.png` 前面。
- 每张拼图最多放 3 个不同文件名。
- 每个图片单元格为 500 × 500 像素。
- 图片按比例缩放并居中放入单元格，不会强制拉伸或裁切。
- 图片会应用 EXIF 方向信息。
- 图片无法读取时显示 `Error`，并在日志中输出具体文件和异常。

## 6. 输出结果

生成文件位于所选图片根目录下的 `_output` 文件夹：

```text
图片根目录/
└─ _output/
   ├─ sheet_01.png
   ├─ sheet_02.png
   └─ sheet_03.png
```

每张图包含最多 3 个文件名对应的比较行；每一列对应一个模型。

再次运行时，如果输出文件名相同，会覆盖已有的同名拼图。旧的、此次没有重新生成的多余 sheet 文件不会自动删除。

## 7. 打包

```text
dotnet publish -c Release
```