using GameSave.Helpers;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 游戏运行状态阶段
/// </summary>
public enum GameRunStatus
{
    /// <summary>空闲</summary>
    Idle,
    /// <summary>正在恢复存档</summary>
    Restoring,
    /// <summary>游戏运行中</summary>
    Running,
    /// <summary>正在备份</summary>
    BackingUp,
    /// <summary>正在上传到云端</summary>
    Uploading,
    /// <summary>完成</summary>
    Completed
}

/// <summary>
/// 游戏运行状态信息
/// </summary>
public class GameStatusInfo
{
    public GameRunStatus Status { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string Message { get; set; } = string.Empty;
    public double? Progress { get; set; }
}

/// <summary>
/// 游戏生命周期管理服务
/// 封装添加游戏、启动游戏、手动备份、恢复存档等核心业务流程
/// </summary>
public class GameService
{
    private readonly ConfigService _configService;
    private readonly LocalStorageService _localStorageService;
    private readonly ProcessMonitorService _processMonitorService;

    /// <summary>游戏进程退出后触发的事件（参数为游戏 ID）</summary>
    public event EventHandler<string>? GameExited;

    /// <summary>备份完成后触发的事件</summary>
    public event EventHandler<SaveFile>? BackupCompleted;

    /// <summary>游戏运行状态变更事件</summary>
    public event EventHandler<GameStatusInfo>? StatusChanged;

    /// <summary>当前是否有游戏正在运行</summary>
    public bool IsGameRunning { get; private set; }

    /// <summary>当前运行中的游戏 ID</summary>
    public string? RunningGameId { get; private set; }

    /// <summary>当前运行中的进程 PID</summary>
    public int RunningProcessId { get; private set; }

    public GameService(ConfigService configService, LocalStorageService localStorageService, ProcessMonitorService processMonitorService)
    {
        _configService = configService;
        _localStorageService = localStorageService;
        _processMonitorService = processMonitorService;
    }

    /// <summary>
    /// 添加游戏全流程
    /// 1. 在工作目录下创建以 GUID 命名的子目录
    /// 2. 检测存档目录是否有文件，有则自动备份为「退出存档」
    /// 3. 保存游戏信息到 config.json
    /// </summary>
    public async Task<Game> AddGameAsync(Game game)
    {
        // 确保 GUID 已分配
        if (string.IsNullOrEmpty(game.Id))
            game.Id = Guid.NewGuid().ToString();

        game.AddedAt = DateTime.Now;

        // 在工作目录下创建游戏子目录
        _configService.GetGameWorkDirectory(game.Id);

        // 检测存档目录是否有文件，有则自动初始备份
        if (Directory.Exists(game.SaveFolderPath) &&
            Directory.EnumerateFiles(game.SaveFolderPath, "*", SearchOption.AllDirectories).Any())
        {
            await _localStorageService.CreateExitSaveAsync(game);
        }

        // 保存到配置
        await _configService.AddGameAsync(game);

        return game;
    }

    /// <summary>
    /// 启动游戏全流程
    /// 1. 检测并恢复最新退出存档
    /// 2. 启动游戏进程
    /// 3. 监测进程退出后自动备份
    /// </summary>
    /// <param name="game">要启动的游戏</param>
    /// <param name="onTarInvalid">tar 文件无效时的回调，返回 true 则继续启动</param>
    /// <returns>操作是否成功</returns>
    public async Task<bool> LaunchGameAsync(Game game, Func<Task<bool>>? onTarInvalid = null)
    {
        if (string.IsNullOrWhiteSpace(game.ProcessPath))
            throw new InvalidOperationException("未设置游戏启动进程路径");

        // 1. 查找最新退出存档
        var latestExitSave = await _localStorageService.GetLatestExitSaveAsync(game.Id);

        if (latestExitSave != null)
        {
            // 校验 .tar 文件有效性
            var isValid = await TarHelper.ValidateTarAsync(latestExitSave.Path);

            if (!isValid)
            {
                // 通知用户存档损坏
                if (onTarInvalid != null)
                {
                    var shouldContinue = await onTarInvalid();
                    if (!shouldContinue) return false;
                }
            }
            else
            {
                // 清空存档目录并恢复
                if (Directory.Exists(game.SaveFolderPath))
                {
                    ClearDirectory(game.SaveFolderPath);
                }
                await TarHelper.ExtractTarAsync(latestExitSave.Path, game.SaveFolderPath);
            }
        }

        // 2. 启动游戏进程
        var process = _processMonitorService.LaunchProcess(game.ProcessPath, game.ProcessArgs);
        if (process == null)
            throw new InvalidOperationException("启动游戏进程失败");

        IsGameRunning = true;
        RunningGameId = game.Id;
        RunningProcessId = process.Id;

        // 通知：游戏运行中
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Running,
            GameName = game.Name,
            GameId = game.Id,
            ProcessId = process.Id,
            Message = $"游戏 {game.Name} 运行中 (PID: {process.Id})"
        });

        // 3. 后台监测进程退出
        _ = Task.Run(async () =>
        {
            await _processMonitorService.WaitForExitAsync(process);

            IsGameRunning = false;
            RunningGameId = null;
            RunningProcessId = 0;

            // 通知：正在备份
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.BackingUp,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"游戏 {game.Name} 已退出，正在备份存档..."
            });

            // 进程退出后自动备份
            SaveFile? save = null;
            try
            {
                if (Directory.Exists(game.SaveFolderPath) &&
                    Directory.EnumerateFiles(game.SaveFolderPath, "*", SearchOption.AllDirectories).Any())
                {
                    var progress = new Progress<double>(p =>
                    {
                        StatusChanged?.Invoke(this, new GameStatusInfo
                        {
                            Status = GameRunStatus.BackingUp,
                            GameName = game.Name,
                            GameId = game.Id,
                            Message = $"游戏 {game.Name} 已退出，正在备份存档...",
                            Progress = p
                        });
                    });

                    save = await _localStorageService.CreateExitSaveAsync(game, progress);
                    BackupCompleted?.Invoke(this, save);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自动备份失败: {ex.Message}");
            }

            // 备份完成后，检测是否需要上传到云端
            bool uploaded = false;
            if (save != null)
            {
                uploaded = await TryUploadToCloudAsync(game, save);
            }

            // 如果没有上传到云端，则手动发送完成状态
            if (!uploaded)
            {
                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Completed,
                    GameName = game.Name,
                    GameId = game.Id,
                    Message = $"游戏 {game.Name} 存档备份完成"
                });

                // 短暂延迟后恢复空闲
                await Task.Delay(3000);
                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Idle,
                    Message = "同步就绪"
                });
            }

            GameExited?.Invoke(this, game.Id);
        });

        return true;
    }

    /// <summary>
    /// 手动备份
    /// </summary>
    public async Task<SaveFile> ManualBackupAsync(Game game, string? saveName = null)
    {
        var name = string.IsNullOrWhiteSpace(saveName) ? "手动存档" : saveName;

        var progress = new Progress<double>(p =>
        {
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.BackingUp,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"正在手动备份 {game.Name}...",
                Progress = p
            });
        });

        var save = await _localStorageService.BackupSaveAsync(game, name, null, progress);
        BackupCompleted?.Invoke(this, save);

        // 手动备份完成后，后台上传到云端（不阻塞备份结果返回）
        _ = Task.Run(async () =>
        {
            try
            {
                await TryUploadToCloudAsync(game, save);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[手动备份] 云端上传异常: {ex.Message}");
            }
        });

        return save;
    }

    /// <summary>
    /// 恢复存档（含运行状态检测）
    /// </summary>
    /// <param name="game">目标游戏</param>
    /// <param name="saveFile">要恢复的存档</param>
    /// <param name="forceRestore">是否强制恢复（跳过运行检测）</param>
    public async Task RestoreSaveAsync(Game game, SaveFile saveFile, bool forceRestore = false)
    {
        if (saveFile.Tag == SaveTag.ExitSave)
            throw new InvalidOperationException("退出存档不支持手动恢复");

        // 检测游戏是否正在运行
        if (!forceRestore && ProcessMonitorService.IsProcessRunning(game.ProcessPath))
        {
            throw new GameRunningException("检测到游戏仍然在运行，除非你非常了解此游戏的存档机制，否则可能导致存档损坏！");
        }

        var progress = new Progress<double>(p =>
        {
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Restoring,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"正在恢复 {game.Name} 的存档...",
                Progress = p
            });
        });

        await _localStorageService.RestoreSaveAsync(saveFile, progress);

        // 通知恢复完成
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Completed,
            GameName = game.Name,
            GameId = game.Id,
            Message = $"已成功恢复 {game.Name} 的存档"
        });

        // 短暂延迟后恢复空闲
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Idle,
                Message = "同步就绪"
            });
        });
    }

    /// <summary>
    /// 删除游戏（含清理工作目录下的所有存档备份）
    /// </summary>
    public async Task DeleteGameAsync(Game game)
    {
        // 直接拼路径，不调用 GetGameWorkDirectory（那个方法会自动创建目录）
        var gameWorkDir = Path.Combine(_configService.WorkDirectory, game.Id);

        if (Directory.Exists(gameWorkDir))
        {
            Directory.Delete(gameWorkDir, true);
            System.Diagnostics.Debug.WriteLine($"已删除存档备份目录: {gameWorkDir}");
        }

        // 从配置中移除
        await _configService.RemoveGameAsync(game.Id);
    }

    /// <summary>
    /// 检查指定游戏是否正在运行
    /// </summary>
    public bool CheckGameRunning(Game game)
    {
        return ProcessMonitorService.IsProcessRunning(game.ProcessPath);
    }

    /// <summary>
    /// 清空目录所有内容
    /// </summary>
    private static void ClearDirectory(string path)
    {
        var di = new DirectoryInfo(path);
        foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Delete();
        }
        foreach (var dir in di.EnumerateDirectories())
        {
            dir.Delete(true);
        }
    }

    /// <summary>
    /// 尝试将存档上传到云端（若游戏已关联云端配置）
    /// </summary>
    private async Task<bool> TryUploadToCloudAsync(Game game, SaveFile save)
    {
        if (string.IsNullOrEmpty(game.CloudConfigId))
            return false;

        var cloudConfig = _configService.GetCloudConfigById(game.CloudConfigId);
        if (cloudConfig == null || !cloudConfig.IsEnabled)
            return false;

        try
        {
            var progressHandler = new Progress<double>(p =>
            {
                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Uploading,
                    GameName = game.Name,
                    GameId = game.Id,
                    Message = $"正在上传 {game.Name} 存档到云端...",
                    Progress = p
                });
            });

            var cloudService = new CloudStorageService(cloudConfig, _configService);
            await cloudService.UploadSaveFileAsync(save, game, progressHandler);

            // 同步上传游戏元数据到云端（确保 game.json 始终最新）
            await cloudService.UploadGameMetadataAsync(game);

            System.Diagnostics.Debug.WriteLine($"[云端同步] {game.Name} 存档已上传到 {cloudConfig.DisplayName}");

            // 通知：上传完成
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Completed,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"{game.Name} 存档已同步到云端"
            });

            // 短暂延迟后恢复空闲
            await Task.Delay(3000);
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Idle,
                Message = "同步就绪"
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[云端同步] 上传失败: {ex.Message}");

            // 上传失败也恢复空闲状态
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Idle,
                Message = $"云端同步失败: {ex.Message}"
            });

            return false;
        }
    }
}

/// <summary>
/// 游戏正在运行异常（用于恢复存档时的安全检查）
/// </summary>
public class GameRunningException : Exception
{
    public GameRunningException(string message) : base(message) { }
}
