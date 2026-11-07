# ComfyCollage

ComfyCollage 是一个 Windows WPF 小工具，用于横向比较多个 AI 绘图模型生成的图片。

## 1. 工作方式

- WPF 负责选择图片根目录、勾选模型、设置参数、启动 Python、显示实时日志与进度。
- Python + Pillow 负责扫描图片、缩放排版和生成拼图。
- 输出目录默认为所选图片根目录下的 `_output`。

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

如果 `python` 不在 PATH 中，可以在界面的「Python」一栏填写 `python.exe` 的完整路径；
留空时会依次尝试 `python`、`py -3`、`python3`。

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

每个模型文件夹代表一个模型。程序按文件名匹配图片：

- 相同文件名会排列在同一行，方便比较不同模型。
- 某个模型缺少该文件时，对应位置显示 `Missing`。
- 根目录下以 `.` 或 `_` 开头的子文件夹会被忽略（因此 `_output` 不会被当成模型）。
- 图片文件名采用自然排序，例如 `2.png` 会排在 `10.png` 前面。

## 4. 支持的图片格式

`.png`、`.jpg`、`.jpeg`、`.webp`、`.bmp`、`.gif`、`.tif`、`.tiff`。

其他文件类型会被忽略。带透明通道的图片会先合成到单元格底色上，不会出现黑边。

## 5. 界面选项

| 选项 | 说明 |
| --- | --- |
| 图片目录 | 可直接拖拽文件夹到窗口 |
| 模型 | 勾选参与对比的文件夹；「上移 / 下移」决定列顺序 |
| 单元格边长 | 64 ~ 4096，默认 500 |
| 每张行数 | 每张拼图放几行；`0` 表示全部放进一张长图 |
| 缩放方式 | 「完整显示」不裁切；「填满并居中裁切」铺满格子 |
| 输出格式 | PNG / JPG / WebP |
| 文件匹配 | 完整文件名；或忽略扩展名（`001.png` 与 `001.jpg` 视为同一张） |
| 间距 | 格子之间与页面边缘的留白 |
| 画质 | JPG / WebP 的画质，1 ~ 100 |
| 输出目录 | 相对图片目录的文件夹名，也可填绝对路径 |
| 只要共有文件 | 仅保留所有模型都存在的文件名 |
| 清理旧图 | 删除本次没有重新生成的旧拼图 |
| 完成后打开目录 | 生成结束后自动打开输出文件夹 |
| 仅预览 | 只打印生成计划，不渲染、不写文件 |

字体、标题栏高度、行高都会按单元格边长自动缩放；中文文件名使用微软雅黑显示。

参数、上次目录和勾选状态会自动保存在：

```text
%APPDATA%\ComfyCollage\settings.json
```

## 6. 命令行用法

脚本也可以脱离界面直接使用，旧的两段式调用保持兼容：

```powershell
python gen_sheets_cli.py "D:\图片"
python gen_sheets_cli.py "D:\图片" --tile 640 --rows 4 --fit cover --format jpg
python gen_sheets_cli.py "D:\图片" --models "wai17,zuki,boleromix" --only-common
python gen_sheets_cli.py "D:\图片" --out "对比图" --clean
python gen_sheets_cli.py "D:\图片" --dry-run
```

可用参数：

```text
--models   a,b,c       指定模型（逗号分隔，顺序即列顺序）
--tile     500         单元格边长
--rows     3           每张行数，0 表示全部
--gap      8           间距
--fit      contain|cover
--format   png|jpg|webp
--quality  92          JPG/WebP 画质
--match    full|stem   文件名匹配方式
--only-common          只保留共有文件
--no-label             不显示文件名
--no-header            不显示模型标题
--out      _output     输出目录（相对根目录或绝对路径）
--prefix   sheet       输出文件名前缀
--clean                删除本次未生成的旧拼图
--dry-run              只打印计划
```

## 7. 输出结果

```text
图片根目录/
└─ _output/
   ├─ sheet_01.png
   ├─ sheet_02.png
   └─ sheet_03.png
```

每张图包含最多 N 个文件名对应的比较行（N 由「每张行数」决定）；每一列对应一个模型。

再次运行时，同名的旧拼图会被覆盖。勾选「清理旧图」后，本次没有生成的旧 `sheet_XX` 会被删除。

## 8. 打包

```text
dotnet publish -c Release
```

应用图标为项目根目录的 `icon.ico`（已通过 `ApplicationIcon` 打包进 exe，并作为 `Resource` 用于窗口图标），
可用 `tools/make_icon.py` 重新生成；`tools/preview_sizes.py` 可输出各小尺寸的辨识度预览。
