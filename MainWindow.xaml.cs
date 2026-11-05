using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace ComfyCollage
{
    public partial class MainWindow : Window
    {
        private bool _isRunning;

        public MainWindow()
        {
            InitializeComponent();
        }

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
                    TxtBase.Text = dialog.FolderName;
                    AppendLog($"[INFO] 已选择目录：{dialog.FolderName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择文件夹失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string pythonScript = Path.Combine(appDirectory, "gen_sheets_cli.py");

            if (!File.Exists(pythonScript))
            {
                MessageBox.Show($"找不到 Python 脚本：\n{pythonScript}\n\n请确保脚本与 exe 一起部署。",
                    "缺少文件", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetRunning(true);
            TxtLog.Clear();
            AppendLog("[INFO] 正在启动 Python……");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    WorkingDirectory = appDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                // -u 关闭 Python stdout/stderr 缓冲，确保运行过程中逐行输出。
                startInfo.ArgumentList.Add("-u");
                startInfo.ArgumentList.Add(pythonScript);
                startInfo.ArgumentList.Add(basePath);

                // 明确指定 Python 输出 UTF-8，防止中文日志乱码。
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUTF8"] = "1";

                using var process = new Process { StartInfo = startInfo };

                var stdoutClosed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var stderrClosed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        stdoutClosed.TrySetResult(true);
                    else
                        Dispatcher.Invoke(new Action(() => AppendLog(args.Data)));
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        stderrClosed.TrySetResult(true);
                    else
                        Dispatcher.Invoke(new Action(() => AppendLog("[STDERR] " + args.Data)));
                };

                if (!process.Start())
                {
                    AppendLog("[ERROR] 无法启动 Python 进程。");
                    return;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task);

                if (process.ExitCode == 0)
                {
                    AppendLog("[SUCCESS] 对比图生成完成。");
                    MessageBox.Show(
                        $"生成完成！\n输出目录：\n{Path.Combine(appDirectory, "_output")}",
                        "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    AppendLog($"[ERROR] Python 进程异常退出，代码：{process.ExitCode}");
                    MessageBox.Show($"生成失败，退出代码：{process.ExitCode}\n请查看日志。",
                        "生成失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                AppendLog("[ERROR] 无法找到 Python。请确认已安装 Python，并且 python 命令已加入 PATH。");
                MessageBox.Show("无法启动 Python。\n请确认已安装 Python，并且 python 命令已加入系统 PATH。",
                    "Python 未找到", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 系统异常：{ex.Message}");
                MessageBox.Show($"运行过程中发生异常：\n{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetRunning(false);
            }
        }

        private void AppendLog(string message)
        {
            TxtLog.AppendText(message + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }

        private void SetRunning(bool running)
        {
            _isRunning = running;
            BtnRun.IsEnabled = !running;
            BtnBrowse.IsEnabled = !running;
            BtnRun.Content = running ? "正在生成，请稍候……" : "自动扫描并生成对比图";
        }
    }
}
