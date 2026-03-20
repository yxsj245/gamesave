using System.Diagnostics;

namespace GameSave.Services;

/// <summary>
/// 进程退出类型
/// </summary>
public enum ProcessExitType
{
    /// <summary>正常退出（进程自然结束）</summary>
    Normal,
    /// <summary>疑似崩溃（Steam stub 快速退出后，真实游戏进程未检测到）</summary>
    CrashOrNotLaunched,
    /// <summary>监测被取消</summary>
    Cancelled
}

/// <summary>
/// 进程监测服务
/// 负责游戏进程的启动、PID 捕获和退出监测
/// 支持 Steam 游戏的 stub 进程快速退出场景
/// </summary>
public class ProcessMonitorService
{
    /// <summary>
    /// stub 进程快速退出的阈值（秒）
    /// 如果进程在此时间内退出，判定为可能是 Steam 游戏的启动器 stub
    /// </summary>
    private const int StubExitThresholdSeconds = 5;

    /// <summary>
    /// 等待 Steam 客户端启动真正游戏进程的延迟时间（毫秒）
    /// </summary>
    private const int SteamLaunchDelayMs = 5000;

    /// <summary>
    /// 进程名轮询间隔（毫秒）
    /// </summary>
    private const int PollingIntervalMs = 2000;

    /// <summary>
    /// 连续未检测到进程的次数阈值，达到此值后确认进程已退出
    /// </summary>
    private const int NotFoundThreshold = 3;

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

        // 默认使用 exe 所在目录作为工作目录
        var workingDirectory = Path.GetDirectoryName(processPath) ?? string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        var process = Process.Start(startInfo);
        return process;
    }

    /// <summary>
    /// 异步等待进程退出，支持 Steam 游戏 stub 进程的自动检测
    /// 当进程在短时间内退出时（疑似 Steam stub），会自动切换到进程名轮询模式
    /// </summary>
    /// <param name="process">要监测的进程</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>进程退出类型</returns>
    public async Task<ProcessExitType> WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
    {
        var processName = process.ProcessName;
        var startTime = DateTime.Now;

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 监测被取消
            return ProcessExitType.Cancelled;
        }

        var elapsed = DateTime.Now - startTime;

        // 如果进程在阈值时间内退出，可能是 Steam 游戏的 stub 进程
        if (elapsed.TotalSeconds < StubExitThresholdSeconds)
        {
            Debug.WriteLine($"[进程监测] 进程 {processName} 在 {elapsed.TotalSeconds:F1} 秒内退出，疑似 Steam stub，切换到轮询模式");

            // 等待 Steam 客户端启动真正的游戏进程
            try
            {
                await Task.Delay(SteamLaunchDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return ProcessExitType.Cancelled;
            }

            // 进入进程名轮询模式
            var exitType = await PollProcessByNameAsync(processName, cancellationToken);

            if (exitType != ProcessExitType.Cancelled)
            {
                ProcessExited?.Invoke(this, process.ExitCode);
            }

            return exitType;
        }

        // 正常退出
        ProcessExited?.Invoke(this, process.ExitCode);
        return ProcessExitType.Normal;
    }

    /// <summary>
    /// 通过进程名轮询检测游戏是否仍在运行
    /// </summary>
    /// <param name="processName">要检测的进程名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>进程退出类型</returns>
    private async Task<ProcessExitType> PollProcessByNameAsync(string processName, CancellationToken cancellationToken)
    {
        int notFoundCount = 0;
        bool everFound = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length > 0)
                {
                    if (!everFound)
                    {
                        Debug.WriteLine($"[进程监测] 轮询模式：检测到进程 {processName} 正在运行");
                        everFound = true;
                    }
                    notFoundCount = 0; // 重置计数
                }
                else
                {
                    notFoundCount++;
                    Debug.WriteLine($"[进程监测] 轮询模式：未检测到进程 {processName}（连续 {notFoundCount}/{NotFoundThreshold} 次）");

                    if (notFoundCount >= NotFoundThreshold)
                    {
                        if (everFound)
                        {
                            // 曾经检测到进程运行，现在连续多次检测不到 → 正常退出
                            Debug.WriteLine($"[进程监测] 进程 {processName} 已正常退出");
                            return ProcessExitType.Normal;
                        }
                        else
                        {
                            // 从未检测到进程 → 疑似崩溃或 Steam 未成功启动游戏
                            Debug.WriteLine($"[进程监测] 进程 {processName} 从未被检测到，疑似崩溃或未成功启动");
                            return ProcessExitType.CrashOrNotLaunched;
                        }
                    }
                }
            }
            finally
            {
                // 释放所有 Process 对象的操作系统句柄，防止内存泄漏
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }

            try
            {
                await Task.Delay(PollingIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return ProcessExitType.Cancelled;
            }
        }

        return ProcessExitType.Cancelled;
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
        var isRunning = processes.Length > 0;
        // 释放所有 Process 对象的操作系统句柄，防止内存泄漏
        foreach (var p in processes)
        {
            p.Dispose();
        }
        return isRunning;
    }

    /// <summary>
    /// 根据进程名检测游戏是否正在运行（通过名称匹配）
    /// </summary>
    public static bool IsProcessRunningByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var processes = Process.GetProcessesByName(processName);
        var isRunning = processes.Length > 0;
        // 释放所有 Process 对象的操作系统句柄，防止内存泄漏
        foreach (var p in processes)
        {
            p.Dispose();
        }
        return isRunning;
    }

    /// <summary>
    /// 根据进程 ID 结束进程
    /// </summary>
    /// <param name="processId">进程ID</param>
    public static void StopProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
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
            Debug.WriteLine($"结束进程失败 PID={processId}: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据进程名结束所有匹配的进程
    /// 用于 Steam 游戏场景下原始 PID 已失效时，通过进程名杀掉真实游戏进程
    /// </summary>
    /// <param name="processName">进程名（不含扩展名）</param>
    public static void StopProcessByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return;

        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true); // 递归结束进程树
                        Debug.WriteLine($"已通过进程名结束进程 {processName} (PID={process.Id})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"结束进程 {processName} (PID={process.Id}) 失败: {ex.Message}");
                }
                finally
                {
                    // 释放 Process 对象的操作系统句柄，防止内存泄漏
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"通过进程名 {processName} 结束进程失败: {ex.Message}");
        }
    }
}
