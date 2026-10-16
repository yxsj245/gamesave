using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace GameSave.Helpers;

/// <summary>
/// 管理员权限辅助类 - 检测和提升权限
/// ETW 内核追踪需要管理员权限才能运行
/// </summary>
[SupportedOSPlatform("windows")]
public static class AdminHelper
{
    /// <summary>
    /// 提权重启时用于等待旧进程退出的命令行参数，其值为待等待的进程 ID
    /// 解析方见 Program.cs 的 WaitForOldProcessIfNeeded
    /// </summary>
    public const string WaitExitArgument = "--wait-exit";

    /// <summary>
    /// 检测当前是否以管理员权限运行
    /// </summary>
    public static bool IsRunAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 以管理员权限重启当前应用
    /// 使用 UAC 提升权限（弹出 UAC 确认对话框）
    /// </summary>
    /// <returns>是否成功启动提权进程</returns>
    public static bool RestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            // 必须保留原始启动参数（--portable-workdir / --workdir 等）：
            // 丢失后提权进程会按默认逻辑重新定位配置文件，工作目录与相关配置整组切换
            var arguments = BuildRestartArguments(
                GetLaunchArguments(Environment.GetCommandLineArgs()),
                Environment.ProcessId);

            var startInfo = CreateRestartStartInfo(exePath, arguments);

            Debug.WriteLine($"[AdminHelper] 提权重启，透传参数个数: {startInfo.ArgumentList.Count}");
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            // 用户取消了 UAC 确认，或其他错误
            Debug.WriteLine($"[AdminHelper] 提权重启失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从 Environment.GetCommandLineArgs 的结果中取出真实启动参数（跳过首项可执行文件路径）
    /// </summary>
    public static IReadOnlyList<string> GetLaunchArguments(string[] commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);

        return commandLineArgs.Length <= 1
            ? Array.Empty<string>()
            : commandLineArgs.Skip(1).ToArray();
    }

    /// <summary>
    /// 构造提权重启使用的命令行参数：保留配置身份参数、剔除 --silent 及旧的 --wait-exit 和其值，最后追加当前进程 ID
    /// 参数名匹配忽略大小写；仅当 --wait-exit 后面确实是一个整数时才把它当作旧值剔除，
    /// 以免误删后续参数（与 Program.cs 中 int.TryParse 的解析口径一致）
    /// </summary>
    /// <param name="originalArguments">原始启动参数（不含可执行文件路径），可为 null</param>
    /// <param name="currentProcessId">当前进程 ID，作为新的等待目标</param>
    public static IReadOnlyList<string> BuildRestartArguments(
        IEnumerable<string>? originalArguments,
        int currentProcessId)
    {
        var retained = new List<string>();

        if (originalArguments is not null)
        {
            var source = originalArguments.Where(argument => argument is not null).ToList();

            for (int i = 0; i < source.Count; i++)
            {
                // 提权由用户交互触发，不能继承开机自启的静默隐藏状态。
                if (string.Equals(source[i], "--silent", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsWaitExitArgument(source[i]))
                {
                    if (i + 1 < source.Count && IsProcessIdToken(source[i + 1]))
                    {
                        i++; // 连同旧值一起剔除
                    }

                    continue;
                }

                retained.Add(source[i]);
            }
        }

        retained.Add(WaitExitArgument);
        retained.Add(currentProcessId.ToString(CultureInfo.InvariantCulture));

        return retained;
    }

    /// <summary>
    /// 构造提权重启所用的进程启动信息（无副作用，可直接单测）
    /// 参数通过 ArgumentList 交给运行时转义，避免手工拼接在中文、空格、尾反斜杠、引号场景下出错
    /// </summary>
    /// <param name="executablePath">要重启的可执行文件路径</param>
    /// <param name="arguments">按原样透传的参数列表</param>
    public static ProcessStartInfo CreateRestartStartInfo(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            // 只有 ShellExecute + runas 才会触发 UAC 提权；
            // 该模式同样支持 ArgumentList（运行时以 PasteArguments 规则转义后传给 ShellExecuteEx）
            UseShellExecute = true,
            Verb = "runas"
        };

        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("启动参数列表不能包含 null 元素。", nameof(arguments));
            }

            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// 判断参数是否为等待旧进程退出的参数（忽略大小写）
    /// </summary>
    private static bool IsWaitExitArgument(string argument)
        => string.Equals(argument, WaitExitArgument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断参数是否可作为进程 ID 解析
    /// </summary>
    private static bool IsProcessIdToken(string argument)
        => int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}
