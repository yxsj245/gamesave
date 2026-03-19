using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 定时备份调度服务
/// 管理每个游戏的定时备份任务，按间隔自动执行备份并控制最大保留数量
/// </summary>
public class ScheduledBackupService
{
    private readonly GameService _gameService;
    private readonly LocalStorageService _localStorageService;
    private readonly ConfigService _configService;

    /// <summary>每个游戏的定时器（key: gameId）</summary>
    private readonly Dictionary<string, Timer> _timers = new();

    /// <summary>防止同一游戏并发备份的锁（key: gameId）</summary>
    private readonly Dictionary<string, SemaphoreSlim> _backupLocks = new();

    /// <summary>定时备份标签名</summary>
    public const string ScheduledBackupTag = "定时备份";

    public ScheduledBackupService(GameService gameService, LocalStorageService localStorageService, ConfigService configService)
    {
        _gameService = gameService;
        _localStorageService = localStorageService;
        _configService = configService;
    }

    /// <summary>
    /// 启动指定游戏的定时备份
    /// </summary>
    public void StartScheduledBackup(Game game)
    {
        if (!game.ScheduledBackupEnabled || game.ScheduledBackupIntervalMinutes <= 0)
            return;

        // 先停止已有的定时器
        StopScheduledBackup(game.Id);

        var intervalMs = game.ScheduledBackupIntervalMinutes * 60 * 1000;

        // 确保有备份锁
        if (!_backupLocks.ContainsKey(game.Id))
        {
            _backupLocks[game.Id] = new SemaphoreSlim(1, 1);
        }

        var timer = new Timer(async _ => await ExecuteBackupAsync(game), null, intervalMs, intervalMs);
        _timers[game.Id] = timer;

        // 更新运行状态（需要调度到 UI 线程）
        var dispatcherQueue = App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue != null)
        {
            dispatcherQueue.TryEnqueue(() => game.IsScheduledBackupRunning = true);
        }
        else
        {
            game.IsScheduledBackupRunning = true;
        }

        System.Diagnostics.Debug.WriteLine($"[定时备份] 已启动 {game.Name} 的定时备份，间隔 {game.ScheduledBackupIntervalMinutes} 分钟");
    }

    /// <summary>
    /// 停止指定游戏的定时备份
    /// </summary>
    public void StopScheduledBackup(string gameId)
    {
        if (_timers.TryGetValue(gameId, out var timer))
        {
            timer.Dispose();
            _timers.Remove(gameId);
        }

        // 更新运行状态（通过 ConfigService 获取 Game 对象）
        var game = _configService.GetGameById(gameId);
        if (game != null)
        {
            var dispatcherQueue = App.MainWindow?.DispatcherQueue;
            if (dispatcherQueue != null)
            {
                dispatcherQueue.TryEnqueue(() => game.IsScheduledBackupRunning = false);
            }
            else
            {
                game.IsScheduledBackupRunning = false;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[定时备份] 已停止 gameId={gameId} 的定时备份");
    }

    /// <summary>
    /// 停止所有定时备份（应用退出时调用）
    /// </summary>
    public void StopAll()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
        _timers.Clear();

        System.Diagnostics.Debug.WriteLine("[定时备份] 已停止所有定时备份");
    }

    /// <summary>
    /// 执行一次定时备份
    /// </summary>
    private async Task ExecuteBackupAsync(Game game)
    {
        // 获取或创建备份锁
        if (!_backupLocks.TryGetValue(game.Id, out var backupLock))
        {
            backupLock = new SemaphoreSlim(1, 1);
            _backupLocks[game.Id] = backupLock;
        }

        // 如果上一次备份还没完成，跳过本次
        if (!await backupLock.WaitAsync(0))
        {
            System.Diagnostics.Debug.WriteLine($"[定时备份] {game.Name} 上次备份尚未完成，跳过本次");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[定时备份] 开始执行 {game.Name} 的定时备份...");

            // 检查存档路径是否有文件（任一路径有文件即可备份，支持目录和文件路径）
            bool anyHasFiles = game.ResolvedSaveFolderPaths.Any(p =>
                (Directory.Exists(p) &&
                Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Any()) ||
                (File.Exists(p) && !Directory.Exists(p)));
            if (!anyHasFiles)
            {
                System.Diagnostics.Debug.WriteLine($"[定时备份] {game.Name} 存档路径无文件，跳过备份");
                return;
            }

            // 执行备份
            await _gameService.ManualBackupAsync(game, ScheduledBackupTag);

            // 清理超出最大数量的旧定时备份
            await CleanupOldBackupsAsync(game);

            System.Diagnostics.Debug.WriteLine($"[定时备份] {game.Name} 定时备份完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[定时备份] {game.Name} 定时备份失败: {ex.Message}");
        }
        finally
        {
            backupLock.Release();
        }
    }

    /// <summary>
    /// 清理超出最大数量的旧定时备份
    /// </summary>
    private async Task CleanupOldBackupsAsync(Game game)
    {
        try
        {
            var allSaves = await _localStorageService.GetSavesAsync(game.Id);

            // 筛选出定时备份的存档（按备份时间降序排列）
            var scheduledSaves = allSaves
                .Where(s => s.Name == ScheduledBackupTag)
                .OrderByDescending(s => s.BackupTime)
                .ToList();

            // 超出最大数量的，删除最旧的
            if (scheduledSaves.Count > game.ScheduledBackupMaxCount)
            {
                var toDelete = scheduledSaves.Skip(game.ScheduledBackupMaxCount).ToList();
                foreach (var save in toDelete)
                {
                    try
                    {
                        await _localStorageService.DeleteSaveAsync(save);
                        System.Diagnostics.Debug.WriteLine($"[定时备份] 已清理旧备份: {save.Name} ({save.DisplayBackupTime})");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[定时备份] 清理旧备份失败: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[定时备份] 清理旧备份异常: {ex.Message}");
        }
    }
}
