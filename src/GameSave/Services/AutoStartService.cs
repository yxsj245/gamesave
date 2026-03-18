using Microsoft.Win32;

namespace GameSave.Services;

/// <summary>
/// 开机自启动管理服务
/// 通过 Windows 注册表 Run 键实现开机自启动
/// </summary>
public static class AutoStartService
{
    /// <summary>注册表 Run 键路径</summary>
    private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>注册表中的应用名称标识</summary>
    private const string AppName = "GameSaveManager";

    /// <summary>静默启动命令行参数（开机自启时使用，启动后直接进入托盘）</summary>
    public const string SilentArg = "--silent";

    /// <summary>
    /// 获取当前可执行文件路径
    /// </summary>
    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? string.Empty;
    }

    /// <summary>
    /// 检查当前是否已设置开机自启动
    /// </summary>
    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启用开机自启动（写入注册表）
    /// 启动参数包含 --silent，使应用启动后直接进入后台托盘
    /// 便携模式下额外包含 --portable-workdir 参数，确保使用正确的工作目录
    /// </summary>
    public static bool Enable()
    {
        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath)) return false;

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return false;

            // 构建启动命令
            var cmdLine = $"\"{exePath}\" {SilentArg}";

            // 便携模式下，需要在开机自启命令中包含工作目录参数
            if (ConfigService.IsPortableMode)
            {
                var workDir = ConfigService.GetPortableWorkDir();
                cmdLine = $"\"{exePath}\" {SilentArg} --portable-workdir \"{workDir}\"";
            }

            key.SetValue(AppName, cmdLine);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 禁用开机自启动（从注册表移除）
    /// </summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return false;

            key.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置开机自启动状态
    /// </summary>
    /// <param name="enabled">是否启用</param>
    /// <returns>操作是否成功</returns>
    public static bool SetAutoStart(bool enabled)
    {
        return enabled ? Enable() : Disable();
    }

    /// <summary>
    /// 检查当前启动参数是否包含 --silent（即开机自启模式）
    /// </summary>
    public static bool IsSilentStart()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Any(a => a.Equals(SilentArg, StringComparison.OrdinalIgnoreCase));
    }
}
