using System.Diagnostics;
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

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--wait-exit {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas" // 触发 UAC 提权
            };

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
}
