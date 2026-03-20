using System.IO.Pipes;

namespace GameSave;

/// <summary>
/// 应用程序入口点
/// 实现单实例检测：如果已有实例在运行，通过命名管道通知已有实例将窗口前置，然后退出新实例
/// </summary>
public static class Program
{
    /// <summary>
    /// 互斥体名称，确保全局唯一（避免与其他程序冲突）
    /// </summary>
    private const string MutexName = "GameSaveManager_SingleInstance_Mutex_F3A2B1C0";

    /// <summary>
    /// 命名管道名称，用于进程间通信
    /// </summary>
    internal const string PipeName = "GameSaveManager_SingleInstance_Pipe_F3A2B1C0";

    [STAThread]
    static void Main(string[] args)
    {
        // 如果是提权重启，先等待旧进程退出再检查互斥锁
        WaitForOldProcessIfNeeded(args);

        // 尝试创建全局互斥体来检测是否已有实例运行
        using var mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // 已有实例在运行，通过命名管道通知它显示窗口
            NotifyExistingInstance();
            return;
        }

        // 没有其他实例，正常启动应用
        // WinUI 3 非打包应用的标准启动方式
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    /// <summary>
    /// 通过命名管道通知已运行的实例将窗口前置
    /// </summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            // 等待最多 3 秒连接到已有实例的管道服务器
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            writer.Write("SHOW");
            writer.Flush();
        }
        catch
        {
            // 连接失败，可能是旧实例正在退出，忽略错误
        }
    }

    /// <summary>
    /// 如果启动参数包含 --wait-exit，等待指定 PID 的旧进程退出
    /// 用于提权重启场景：新进程需等旧进程释放互斥锁后才能正常获取
    /// </summary>
    private static void WaitForOldProcessIfNeeded(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--wait-exit" && int.TryParse(args[i + 1], out int oldPid))
            {
                try
                {
                    var oldProcess = System.Diagnostics.Process.GetProcessById(oldPid);
                    // 最多等待 5 秒
                    oldProcess.WaitForExit(5000);
                }
                catch
                {
                    // 进程已退出或无法获取，继续
                }
                break;
            }
        }
    }
}
