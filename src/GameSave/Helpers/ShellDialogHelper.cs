using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace GameSave.Helpers
{
    /// <summary>
    /// Shell 相关帮助方法：
    /// 1. 优先使用 WinRT 选择器
    /// 2. 失败时自动降级到 Win32 对话框，兼容部分掌机/精简系统环境
    /// 3. 统一封装资源管理器打开逻辑
    /// </summary>
    public static class ShellDialogHelper
    {
        /// <summary>选择单个文件，失败时自动回退到 Win32 对话框</summary>
        public static async Task<string?> PickFileAsync(
            Microsoft.UI.Xaml.Window? ownerWindow,
            IEnumerable<string> fileTypes,
            Windows.Storage.Pickers.PickerLocationId startLocation = Windows.Storage.Pickers.PickerLocationId.Desktop)
        {
            var normalizedTypes = NormalizeFileTypes(fileTypes);

            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = startLocation
                };

                foreach (var fileType in normalizedTypes)
                {
                    picker.FileTypeFilter.Add(fileType);
                }

                InitializePicker(ownerWindow, picker);

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShellDialogHelper] WinRT 文件选择器打开失败，改用 Win32 备用对话框: {ex}");
                return PickFileWithWin32Dialog(ownerWindow, normalizedTypes);
            }
        }

        /// <summary>选择单个文件夹，失败时自动回退到 Win32 对话框</summary>
        public static async Task<string?> PickFolderAsync(
            Microsoft.UI.Xaml.Window? ownerWindow,
            Windows.Storage.Pickers.PickerLocationId startLocation = Windows.Storage.Pickers.PickerLocationId.Desktop)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker
                {
                    SuggestedStartLocation = startLocation
                };
                picker.FileTypeFilter.Add("*");

                InitializePicker(ownerWindow, picker);

                var folder = await picker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShellDialogHelper] WinRT 文件夹选择器打开失败，改用 Win32 备用对话框: {ex}");
                return PickFolderWithWin32Dialog(ownerWindow);
            }
        }

        /// <summary>在资源管理器中打开目录或定位文件</summary>
        public static void OpenPathInExplorer(string path)
        {
            var fullPath = Path.GetFullPath(path);

            if (Directory.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{fullPath}\"",
                    UseShellExecute = true
                });
                return;
            }

            throw new FileNotFoundException("目标路径不存在，无法打开资源管理器。", fullPath);
        }

        /// <summary>初始化 WinRT Picker 的宿主窗口句柄</summary>
        private static void InitializePicker(Microsoft.UI.Xaml.Window? ownerWindow, object picker)
        {
            if (ownerWindow == null)
                return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(ownerWindow);
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
        }

        /// <summary>规范化文件类型列表，确保 WinRT 与 Win32 都可使用</summary>
        private static List<string> NormalizeFileTypes(IEnumerable<string> fileTypes)
        {
            var normalized = fileTypes
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
            {
                normalized.Add("*");
            }

            if (!normalized.Contains("*", StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add("*");
            }

            return normalized;
        }

        /// <summary>使用 Win32 文件对话框作为降级方案</summary>
        private static string? PickFileWithWin32Dialog(Microsoft.UI.Xaml.Window? ownerWindow, IReadOnlyList<string> fileTypes)
        {
            return RunPowerShellDialog(BuildOpenFileDialogScript(fileTypes));
        }

        /// <summary>使用 Win32 文件夹对话框作为降级方案</summary>
        private static string? PickFolderWithWin32Dialog(Microsoft.UI.Xaml.Window? ownerWindow)
        {
            return RunPowerShellDialog(BuildOpenFolderDialogScript());
        }

        /// <summary>把 WinRT 文件类型列表转换为 Win32 过滤器字符串</summary>
        private static string BuildWin32Filter(IReadOnlyList<string> fileTypes)
        {
            var specificPatterns = fileTypes
                .Where(type => !string.Equals(type, "*", StringComparison.OrdinalIgnoreCase))
                .Select(type => type.StartsWith('.') ? $"*{type}" : type)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (specificPatterns.Count == 0)
            {
                return "所有文件 (*.*)|*.*";
            }

            var joinedPatterns = string.Join(";", specificPatterns);
            return $"支持的文件 ({joinedPatterns})|{joinedPatterns}|所有文件 (*.*)|*.*";
        }

        /// <summary>构建 PowerShell 文件选择脚本</summary>
        private static string BuildOpenFileDialogScript(IReadOnlyList<string> fileTypes)
        {
            var filter = BuildWin32Filter(fileTypes).Replace("'", "''");

            return $@"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$dialog = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Title = '请选择文件'
$dialog.CheckFileExists = $true
$dialog.Multiselect = $false
$dialog.InitialDirectory = [Environment]::GetFolderPath('Desktop')
$dialog.Filter = '{filter}'
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{
    Write-Output $dialog.FileName
}}";
        }

        /// <summary>构建 PowerShell 文件夹选择脚本</summary>
        private static string BuildOpenFolderDialogScript()
        {
            return """
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = '请选择文件夹'
$dialog.SelectedPath = [Environment]::GetFolderPath('Desktop')
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    Write-Output $dialog.SelectedPath
}
""";
        }

        /// <summary>运行 PowerShell 降级对话框并读取结果</summary>
        private static string? RunPowerShellDialog(string script)
        {
            try
            {
                var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process == null)
                    return null;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                process.WaitForExit();

                var output = outputTask.GetAwaiter().GetResult().Trim();
                var error = errorTask.GetAwaiter().GetResult().Trim();

                if (process.ExitCode != 0)
                {
                    Debug.WriteLine($"[ShellDialogHelper] PowerShell 备用对话框启动失败: {error}");
                    return null;
                }

                return string.IsNullOrWhiteSpace(output) ? null : output;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShellDialogHelper] PowerShell 备用对话框调用失败: {ex}");
                return null;
            }
        }
    }
}
