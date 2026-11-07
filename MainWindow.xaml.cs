using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ComfyCollage
{
    public partial class MainWindow : Window
    {
        private const string DefaultOutputFolder = "_output";
        private const int MaxLogLength = 400_000;

        private readonly ObservableCollection<ModelInfo> _models = new();
        private Process? _process;
        private bool _isRunning;
        private bool _cancelRequested;
        private string _scriptPath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            LstModels.ItemsSource = _models;
        }

        // ---------------- 生命周期 ----------------

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = SettingsStore.Load();
            ApplySettings(settings);
            AppendLog("[INFO] 就绪。参数会自动保存，下次启动沿用。");

            if (!string.IsNullOrWhiteSpace(TxtBase.Text) && Directory.Exists(TxtBase.Text.Trim()))
            {
                RefreshModels();
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show("任务还在进行中，确定要退出吗？", "确认退出",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                KillProcess();
            }

            try
            {
                SettingsStore.Save(CollectSettings());
            }
            catch
            {
                // 配置保存失败不应阻止关闭。
            }
        }

        // ---------------- 图标 / 目录选择 ----------------

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "请选择包含各个模型文件夹的根目录"
                };

                if (dialog.ShowDialog() == true)
                {
                    SetBaseDirectory(dialog.FolderName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择文件夹失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetBaseDirectory(string path)
        {
            TxtBase.Text = path;
            TxtBase.ToolTip = path;
            _models.Clear();
            AppendLog($"[INFO] 已选择目录：{path}");
            RefreshModels();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (_isRunning || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] items)
                return;

            var directory = items.FirstOrDefault(Directory.Exists);
            if (directory == null)
            {
                AppendLog("[WARN] 拖入的内容不是文件夹。");
                return;
            }

            SetBaseDirectory(directory);
        }

        // ---------------- 模型列表 ----------------

        private void RefreshModels()
        {
            string basePath = TxtBase.Text.Trim();
            var discovered = new List<string>();

            if (Directory.Exists(basePath))
            {
                try
                {
                    discovered = new DirectoryInfo(basePath).GetDirectories()
                        .Where(d => !d.Name.StartsWith(".", StringComparison.Ordinal)
                                    && !d.Name.StartsWith("_", StringComparison.Ordinal))
                        .Select(d => d.Name)
                        .ToList();
                }
                catch (Exception ex)
                {
                    AppendLog($"[ERROR] 无法读取目录：{ex.Message}");
                }
            }

            var previousOrder = _models.Select(m => m.Name).ToList();
            var previousSelected = _models.Where(m => m.IsSelected)
                .Select(m => m.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool hadPrevious = previousOrder.Count > 0;

            var available = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ordered = previousOrder.Where(name => available.Contains(name)).ToList();
            var newcomers = discovered
                .Where(name => !ordered.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            newcomers.Sort(NaturalComparer.Instance);
            ordered.AddRange(newcomers);

            _models.Clear();
            foreach (var name in ordered)
            {
                var info = new ModelInfo(name)
                {
                    IsSelected = !hadPrevious || previousSelected.Contains(name)
                };
                info.PropertyChanged += (_, _) => UpdateModelCount();
                _models.Add(info);
            }

            TxtModelCount.Text = _models.Count == 0
                ? "没有找到模型文件夹（以 . 或 _ 开头的文件夹会被忽略）。"
                : $"共 {_models.Count} 个模型，已勾选 {_models.Count(m => m.IsSelected)} 个。";
        }

        private void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
        {
            RefreshModels();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var model in _models)
                model.IsSelected = true;
            UpdateModelCount();
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var model in _models)
                model.IsSelected = false;
            UpdateModelCount();
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(-1);
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(1);
        }

        private void MoveSelected(int delta)
        {
            int index = LstModels.SelectedIndex;
            if (index < 0)
                return;

            int target = index + delta;
            if (target < 0 || target >= _models.Count)
                return;

            var item = _models[index];
            _models.Move(index, target);
            LstModels.SelectedIndex = target;
            LstModels.ScrollIntoView(item);
        }

        private void UpdateModelCount()
        {
            TxtModelCount.Text = _models.Count == 0
                ? "没有找到模型文件夹（以 . 或 _ 开头的文件夹会被忽略）。"
                : $"共 {_models.Count} 个模型，已勾选 {_models.Count(m => m.IsSelected)} 个。";
        }

        // ---------------- 执行 ----------------

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            string basePath = TxtBase.Text.Trim();
            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
            {
                MessageBox.Show("请先选择有效的图片根目录！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_models.Count > 0)
            {
                var selected = _models.Where(m => m.IsSelected).Select(m => m.Name).ToList();
                if (selected.Count == 0)
                {
                    MessageBox.Show("请至少勾选一个模型。", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (!TryGetInt(CmbTile.Text, 64, 4096, "单元格边长", out int tile))
                return;
            if (!TryGetInt(CmbRows.Text, 0, 500, "每张行数", out int rows))
                return;
            if (!TryGetInt(TxtGap.Text, 0, 200, "间距", out int gap))
                return;
            if (!TryGetInt(TxtQuality.Text, 1, 100, "画质", out int quality))
                return;

            _scriptPath = ResolveScriptPath();
            if (!File.Exists(_scriptPath))
            {
                MessageBox.Show(
                    $"找不到 Python 脚本：\n{_scriptPath}\n\n请确保 gen_sheets_cli.py 与 exe 一起部署。",
                    "缺少文件", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var args = BuildArguments(basePath, tile, rows, gap, quality);

            SaveSettingsQuietly();
            SetRunning(true);
            TxtLog.Clear();
            ResetProgress();
            AppendLog($"[INFO] 脚本：{_scriptPath}");
            AppendLog($"[INFO] 参数：{string.Join(" ", args.Select(QuoteIfNeeded))}");

            try
            {
                await ExecuteAsync(args);
            }
            finally
            {
                SetRunning(false);
                UpdateModelCount();
            }
        }

        private List<string> BuildArguments(string basePath, int tile, int rows, int gap, int quality)
        {
            string fit = CmbFit.SelectedIndex == 1 ? "cover" : "contain";
            string format = CmbFormat.SelectedIndex switch
            {
                1 => "jpg",
                2 => "webp",
                _ => "png"
            };
            string match = CmbMatch.SelectedIndex == 1 ? "stem" : "full";

            var args = new List<string>
            {
                basePath,
                "--tile", tile.ToString(),
                "--rows", rows.ToString(),
                "--gap", gap.ToString(),
                "--fit", fit,
                "--format", format,
                "--quality", quality.ToString(),
                "--match", match,
                "--out", TxtOutDir.Text.Trim()
            };

            var selected = _models.Where(m => m.IsSelected).Select(m => m.Name).ToList();
            if (selected.Count > 0)
            {
                args.Add("--models");
                args.Add(string.Join(",", selected));
            }

            if (ChkOnlyCommon.IsChecked == true) args.Add("--only-common");
            if (ChkHeader.IsChecked != true) args.Add("--no-header");
            if (ChkLabel.IsChecked != true) args.Add("--no-label");
            if (ChkClean.IsChecked == true) args.Add("--clean");
            if (ChkPreview.IsChecked == true) args.Add("--dry-run");

            return args;
        }

        private async Task ExecuteAsync(List<string> args)
        {
            _cancelRequested = false;

            var candidates = new List<(string Exe, List<string> Prefix)>();
            string configuredPython = TxtPython.Text.Trim();
            if (configuredPython.Length > 0)
                candidates.Add((configuredPython, new List<string>()));
            candidates.Add(("python", new List<string>()));
            candidates.Add(("py", new List<string> { "-3" }));
            candidates.Add(("python3", new List<string>()));

            int? exitCode = null;
            foreach (var candidate in candidates)
            {
                if (_cancelRequested)
                    break;

                exitCode = await TryRunAsync(candidate.Exe, candidate.Prefix, args);
                if (exitCode != null)
                    break;
            }

            if (exitCode == null)
            {
                if (_cancelRequested)
                    return;

                AppendLog("[ERROR] 无法启动 Python。请确认已安装 Python，并把 python 加入 PATH，或在参数里指定 python.exe 路径。");
                MessageBox.Show("无法启动 Python。\n请确认已安装 Python 并加入系统 PATH，或在参数中填写 python.exe 路径。",
                    "Python 未找到", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_cancelRequested)
            {
                AppendLog("[INFO] 任务已取消。");
                return;
            }

            if (exitCode.Value == 0)
            {
                string outputDir = ResolveOutputDirectory();
                if (ChkPreview.IsChecked == true)
                {
                    AppendLog("[SUCCESS] 预览完成，没有写入任何文件。");
                    return;
                }

                AppendLog("[SUCCESS] 处理完成。");
                AppendLog($"[INFO] 输出目录：{outputDir}");

                if (ChkOpenAfter.IsChecked == true && Directory.Exists(outputDir))
                    OpenDirectory(outputDir);
            }
            else
            {
                AppendLog($"[ERROR] Python 进程异常退出，代码：{exitCode.Value}");
                MessageBox.Show($"生成失败，退出代码：{exitCode.Value}\n请查看下方日志。",
                    "生成失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<int?> TryRunAsync(string exe, List<string> prefixArgs, List<string> args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(_scriptPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            foreach (var item in prefixArgs)
                startInfo.ArgumentList.Add(item);

            // -u 关闭 Python 缓冲，保证日志逐行输出。
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(_scriptPath);
            foreach (var item in args)
                startInfo.ArgumentList.Add(item);

            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONUTF8"] = "1";

            Process? process = null;
            try
            {
                process = new Process { StartInfo = startInfo };

                var stdoutClosed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var stderrClosed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        stdoutClosed.TrySetResult(true);
                    else
                        HandleOutputLine(e.Data, isError: false);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        stderrClosed.TrySetResult(true);
                    else
                        HandleOutputLine(e.Data, isError: true);
                };

                if (!process.Start())
                {
                    process.Dispose();
                    return null;
                }

                _process = process;
                AppendLog("[INFO] 正在启动 Python……");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                await Task.WhenAny(
                    Task.WhenAll(stdoutClosed.Task, stderrClosed.Task),
                    Task.Delay(5000));

                return process.ExitCode;
            }
            catch (Win32Exception)
            {
                // 该解释器不可用，交给下一个候选。
                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 启动 {exe} 失败：{ex.Message}");
                return null;
            }
            finally
            {
                if (ReferenceEquals(_process, process))
                    _process = null;
                process?.Dispose();
            }
        }

        private void HandleOutputLine(string line, bool isError)
        {
            if (!isError && line.StartsWith("[PROGRESS] ", StringComparison.Ordinal))
            {
                var payload = line.Substring("[PROGRESS] ".Length);
                var parts = payload.Split('/');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int done)
                    && int.TryParse(parts[1], out int total)
                    && total > 0)
                {
                    if (Dispatcher.CheckAccess())
                    {
                        PrgBar.Maximum = total;
                        PrgBar.Value = Math.Min(done, total);
                        TxtProgress.Text = $"{Math.Min(done, total)}/{total}";
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            PrgBar.Maximum = total;
                            PrgBar.Value = Math.Min(done, total);
                            TxtProgress.Text = $"{Math.Min(done, total)}/{total}";
                        }));
                    }
                }
                return;
            }

            AppendLog(isError ? "[STDERR] " + line : line);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning || _process == null)
                return;

            _cancelRequested = true;
            AppendLog("[INFO] 正在取消任务……");
            KillProcess();
        }

        private void KillProcess()
        {
            var process = _process;
            if (process == null)
                return;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                AppendLog($"[WARN] 结束进程失败：{ex.Message}");
            }
        }

        private void BtnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            string outputDir = ResolveOutputDirectory();
            if (!Directory.Exists(outputDir))
            {
                MessageBox.Show($"输出目录还不存在：\n{outputDir}", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenDirectory(outputDir);
        }

        private void OpenDirectory(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[WARN] 无法打开目录：{ex.Message}");
            }
        }

        private string ResolveOutputDirectory()
        {
            string basePath = TxtBase.Text.Trim();
            string folder = TxtOutDir.Text.Trim();
            if (folder.Length == 0)
                folder = DefaultOutputFolder;

            try
            {
                return Path.GetFullPath(Path.Combine(basePath, folder));
            }
            catch
            {
                return Path.Combine(basePath, folder);
            }
        }

        private string ResolveScriptPath()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string direct = Path.Combine(appDirectory, "gen_sheets_cli.py");
            if (File.Exists(direct))
                return direct;

            string current = Path.Combine(Environment.CurrentDirectory, "gen_sheets_cli.py");
            if (File.Exists(current))
                return current;

            // 从 bin/Debug 之类的子目录运行时，向上查找源码目录。
            var directory = new DirectoryInfo(appDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "gen_sheets_cli.py");
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }

            return direct;
        }

        private static string QuoteIfNeeded(string value)
        {
            return value.Contains(' ') ? $"\"{value}\"" : value;
        }

        private bool TryGetInt(string raw, int min, int max, string label, out int value)
        {
            value = 0;
            if (!int.TryParse(raw?.Trim(), out int parsed))
            {
                MessageBox.Show($"{label} 需要填写整数。", "参数错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (parsed < min || parsed > max)
            {
                MessageBox.Show($"{label} 需要在 {min} ~ {max} 之间。", "参数错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            value = parsed;
            return true;
        }

        // ---------------- 界面状态 ----------------

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            if (TxtLog.Text.Length > MaxLogLength)
            {
                TxtLog.Text = TxtLog.Text.Substring(TxtLog.Text.Length - MaxLogLength / 2);
            }

            TxtLog.AppendText(message + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }

        private void ResetProgress()
        {
            PrgBar.Maximum = 100;
            PrgBar.Value = 0;
            TxtProgress.Text = string.Empty;
        }

        private void SetRunning(bool running)
        {
            _isRunning = running;
            BtnRun.IsEnabled = !running;
            BtnBrowse.IsEnabled = !running;
            BtnCancel.IsEnabled = running;
            BtnRun.Content = running ? "正在生成，请稍候……" : "生成对比图";
        }

        // ---------------- 配置持久化 ----------------

        private AppSettings CollectSettings()
        {
            return new AppSettings
            {
                LastDirectory = TxtBase.Text.Trim(),
                TileSize = int.TryParse(CmbTile.Text.Trim(), out int tile) ? tile : 500,
                Rows = int.TryParse(CmbRows.Text.Trim(), out int rows) ? rows : 3,
                Gap = int.TryParse(TxtGap.Text.Trim(), out int gap) ? gap : 8,
                Quality = int.TryParse(TxtQuality.Text.Trim(), out int quality) ? quality : 92,
                FitMode = CmbFit.SelectedIndex,
                Format = CmbFormat.SelectedIndex,
                Match = CmbMatch.SelectedIndex,
                OutputFolder = TxtOutDir.Text.Trim(),
                PythonPath = TxtPython.Text.Trim(),
                ShowHeader = ChkHeader.IsChecked == true,
                ShowLabels = ChkLabel.IsChecked == true,
                OnlyCommon = ChkOnlyCommon.IsChecked == true,
                Clean = ChkClean.IsChecked == true,
                OpenAfter = ChkOpenAfter.IsChecked == true
            };
        }

        private void ApplySettings(AppSettings settings)
        {
            TxtBase.Text = settings.LastDirectory ?? string.Empty;
            TxtBase.ToolTip = TxtBase.Text;
            CmbTile.Text = Math.Clamp(settings.TileSize, 64, 4096).ToString();
            CmbRows.Text = Math.Clamp(settings.Rows, 0, 500).ToString();
            TxtGap.Text = Math.Clamp(settings.Gap, 0, 200).ToString();
            TxtQuality.Text = Math.Clamp(settings.Quality, 1, 100).ToString();
            CmbFit.SelectedIndex = Math.Clamp(settings.FitMode, 0, 1);
            CmbFormat.SelectedIndex = Math.Clamp(settings.Format, 0, 2);
            CmbMatch.SelectedIndex = Math.Clamp(settings.Match, 0, 1);
            TxtOutDir.Text = string.IsNullOrWhiteSpace(settings.OutputFolder)
                ? DefaultOutputFolder
                : settings.OutputFolder;
            TxtPython.Text = settings.PythonPath ?? string.Empty;
            ChkHeader.IsChecked = settings.ShowHeader;
            ChkLabel.IsChecked = settings.ShowLabels;
            ChkOnlyCommon.IsChecked = settings.OnlyCommon;
            ChkClean.IsChecked = settings.Clean;
            ChkOpenAfter.IsChecked = settings.OpenAfter;
        }

        private void SaveSettingsQuietly()
        {
            try
            {
                SettingsStore.Save(CollectSettings());
            }
            catch
            {
                // 忽略保存失败。
            }
        }

        // ---------------- 内部类型 ----------------

        private sealed class ModelInfo : INotifyPropertyChanged
        {
            private bool _isSelected = true;

            public ModelInfo(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private sealed class AppSettings
        {
            public string? LastDirectory { get; set; }
            public int TileSize { get; set; } = 500;
            public int Rows { get; set; } = 3;
            public int Gap { get; set; } = 8;
            public int Quality { get; set; } = 92;
            public int FitMode { get; set; }
            public int Format { get; set; }
            public int Match { get; set; }
            public string OutputFolder { get; set; } = DefaultOutputFolder;
            public string? PythonPath { get; set; }
            public bool ShowHeader { get; set; } = true;
            public bool ShowLabels { get; set; } = true;
            public bool OnlyCommon { get; set; }
            public bool Clean { get; set; }
            public bool OpenAfter { get; set; }
        }

        private static class SettingsStore
        {
            private static readonly string SettingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ComfyCollage");

            private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

            private static readonly JsonSerializerOptions Options = new()
            {
                WriteIndented = true
            };

            public static AppSettings Load()
            {
                try
                {
                    if (!File.Exists(SettingsFile))
                        return new AppSettings();

                    string json = File.ReadAllText(SettingsFile, Encoding.UTF8);
                    return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }

            public static void Save(AppSettings settings)
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(SettingsFile, json, new UTF8Encoding(false));
            }
        }
    }

    /// <summary>与 Python 版一致的“自然排序”：2 排在 10 前面。</summary>
    internal sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int startX = i, startY = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;

                    string numberX = x.Substring(startX, i - startX).TrimStart('0');
                    string numberY = y.Substring(startY, j - startY).TrimStart('0');

                    if (numberX.Length != numberY.Length)
                        return numberX.Length - numberY.Length;

                    int compare = string.CompareOrdinal(numberX, numberY);
                    if (compare != 0)
                        return compare;
                }
                else
                {
                    int compare = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (compare != 0)
                        return compare;

                    i++;
                    j++;
                }
            }

            return (x.Length - i) - (y.Length - j);
        }
    }
}
