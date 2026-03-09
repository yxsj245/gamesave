using System.Diagnostics;

namespace GameSave.Services;

/// <summary>
/// 进程监测服务
/// 负责游戏进程的启动、PID 捕获和退出监测
/// </summary>
public class ProcessMonitorService
{
    /// <summary>
    /// 进程退出时触发的事件
    /// </summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>
    /// 启动游戏进程并监测其生命周期
    /// </summary>
    /// <param name="processPath">可执行文件路径</param>
    /// <param name="arguments">启动参数（可选）</param>
    /// <returns>启动的进程对象</returns>
    public Process? LaunchProcess(string processPath, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            throw new ArgumentException("进程路径不能为空", nameof(processPath));

        if (!File.Exists(processPath))
            throw new FileNotFoundException($"可执行文件不存在: {processPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        return process;
    }

    /// <summary>
    /// 异步等待进程退出
    /// </summary>
    /// <param name="process">要监测的进程</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            ProcessExited?.Invoke(this, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            // 监测被取消
        }
    }

    /// <summary>
    /// 根据进程名检测游戏是否正在运行
    /// </summary>
    /// <param name="processPath">可执行文件路径</param>
    /// <returns>是否有匹配的进程在运行</returns>
    public static bool IsProcessRunning(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        var processName = Path.GetFileNameWithoutExtension(processPath);
        var processes = Process.GetProcessesByName(processName);
        return processes.Length > 0;
    }

    /// <summary>
    /// 根据进程名检测游戏是否正在运行（通过名称匹配）
    /// </summary>
    public static bool IsProcessRunningByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var processes = Process.GetProcessesByName(processName);
        return processes.Length > 0;
    }

    /// <summary>
    /// 根据进程 ID 结束进程
    /// </summary>
    /// <param name="processId">进程ID</param>
    public static void StopProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(true); // 递归结束进程树
            }
        }
        catch (ArgumentException)
        {
            // 进程可能已经退出，引发异常可忽略
        }
        catch (InvalidOperationException)
        {
            // 同上
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"结束进程失败 PID={processId}: {ex.Message}");
        }
    }
}
