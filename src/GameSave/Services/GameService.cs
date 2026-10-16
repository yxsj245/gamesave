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
    /// <summary>正在启动游戏（检查存档、恢复存档、启动进程）</summary>
    Launching,
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
public class GameService : IQuickSaveGameService
{
    /// <summary>存档冲突时"另存为新存档"使用的存档名称</summary>
    private const string ConflictSaveCopyName = "存档副本";

    private readonly ConfigService _configService;
    private readonly LocalStorageService _localStorageService;
    private readonly ProcessMonitorService _processMonitorService;

    /// <summary>游戏进程退出后触发的事件（参数为游戏 ID）</summary>
    public event EventHandler<string>? GameExited;

    /// <summary>备份完成后触发的事件</summary>
    public event EventHandler<SaveFile>? BackupCompleted;

    /// <summary>游戏运行状态变更事件</summary>
    public event EventHandler<GameStatusInfo>? StatusChanged;

    /// <summary>检测到游戏崩溃或未成功启动时触发的事件（参数为游戏名称）</summary>
    public event EventHandler<string>? GameCrashDetected;

    /// <summary>自动备份失败时触发的事件（参数为游戏名称和错误信息）</summary>
    public event EventHandler<(string GameName, string ErrorMessage)>? BackupFailed;

    /// <summary>进程启动失败/崩溃时触发的事件（参数为错误信息）</summary>
    public event EventHandler<string>? ProcessLaunchFailed;

    /// <summary>当前是否有游戏正在运行</summary>
    public bool IsGameRunning { get; private set; }

    /// <summary>当前运行中的游戏 ID</summary>
    public string? RunningGameId { get; private set; }

    /// <summary>当前运行中的主进程 PID</summary>
    public int RunningProcessId { get; private set; }

    /// <summary>当前运行中的所有进程 PID 列表</summary>
    public List<int> RunningProcessIds { get; private set; } = new();

    /// <summary>当前运行中的主进程名（不含扩展名）</summary>
    public string? RunningProcessName { get; private set; }

    /// <summary>当前运行中的所有进程名列表</summary>
    public List<string> RunningProcessNames { get; private set; } = new();

    /// <summary>最近一次启动失败的错误信息（用于 ViewModel 读取显示）</summary>
    public string? LastLaunchError { get; private set; }

    public GameService(ConfigService configService, LocalStorageService localStorageService, ProcessMonitorService processMonitorService)
    {
        _configService = configService;
        _localStorageService = localStorageService;
        _processMonitorService = processMonitorService;
    }

    /// <summary>获取定时备份服务（延迟获取，避免循环依赖）</summary>
    private ScheduledBackupService ScheduledBackup => App.ScheduledBackupService;

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
        // 添加游戏时游戏必定未在运行中（刚添加），有进程路径的可以用直接模式
        // 无进程路径的无法判断游戏是否在外部运行，走热备份
        bool anyHasFiles = game.ResolvedSaveFolderPaths.Any(p =>
            (Directory.Exists(p) &&
            Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Any()) ||
            (File.Exists(p) && !Directory.Exists(p)));
        if (anyHasFiles)
        {
            bool needHotBackup = !game.HasAnyLaunchMethod;
            await _localStorageService.CreateExitSaveAsync(game, useHotBackup: needHotBackup);
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
    public async Task<bool> LaunchGameAsync(Game game, Func<Task<bool>>? onTarInvalid = null,
        Func<Models.SaveConflictReport, Task<Models.ConflictResolution>>? onSaveConflict = null)
    {
        // 验证启动方式
        if (game.LaunchViaSteam)
        {
            if (string.IsNullOrWhiteSpace(game.SteamAppId))
                throw new InvalidOperationException("未设置 Steam App ID");
            // Steam 模式下进程路径可选：有则监测退出自动备份，无则仅启动不监测
        }
        else
        {
            if (string.IsNullOrWhiteSpace(game.ProcessPath))
                throw new InvalidOperationException("未设置游戏启动进程路径");
        }

        // 清除上次启动错误
        LastLaunchError = null;

        // 通知：开始启动游戏流程
        game.LaunchStatusMessage = $"正在检查本地存档...";
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Launching,
            GameName = game.Name,
            GameId = game.Id,
            Message = $"正在检查 {game.Name} 的本地存档...",
            Progress = 0
        });

        // 1. 查找最新退出存档
        var latestExitSave = await _localStorageService.GetLatestExitSaveAsync(game.Id);

        // 更新进度：存档检查完成
        game.LaunchStatusMessage = latestExitSave != null
            ? "发现退出存档，正在校验..."
            : "无本地存档，准备启动...";
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Launching,
            GameName = game.Name,
            GameId = game.Id,
            Message = latestExitSave != null
                ? $"发现 {game.Name} 的退出存档，正在校验..."
                : $"{game.Name} 无本地存档，准备启动...",
            Progress = 20
        });

        if (latestExitSave != null)
        {
            // 校验 .tar 文件有效性
            var isValid = await TarHelper.ValidateTarAsync(latestExitSave.Path);

            // 更新进度：校验完成
            game.LaunchStatusMessage = isValid
                ? "存档校验通过，正在恢复..."
                : "存档校验失败";
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Launching,
                GameName = game.Name,
                GameId = game.Id,
                Message = isValid
                    ? $"{game.Name} 存档校验通过，正在恢复..."
                    : $"{game.Name} 存档校验失败",
                Progress = 40
            });

            if (!isValid)
            {
                // 通知用户存档损坏
                if (onTarInvalid != null)
                {
                    var shouldContinue = await onTarInvalid();
                    if (!shouldContinue)
                    {
                        // 用户取消，恢复空闲状态
                        game.LaunchStatusMessage = string.Empty;
                        StatusChanged?.Invoke(this, new GameStatusInfo
                        {
                            Status = GameRunStatus.Idle,
                            GameName = game.Name,
                            GameId = game.Id,
                            Message = "启动已取消"
                        });
                        return false;
                    }
                }
            }
            else
            {
                // 存档冲突检测：对比退出存档与当前存档目录
                bool skipRestore = false;
                if (onSaveConflict != null)
                {
                    game.LaunchStatusMessage = "正在对比存档文件...";
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Launching,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = $"正在对比 {game.Name} 的存档文件...",
                        Progress = 45
                    });

                    var conflictReport = await SaveFileComparer.CompareAsync(
                        latestExitSave.Path,
                        game.SaveFolderPaths,
                        game.Id,
                        game.Name);

                    if (conflictReport.HasConflict)
                    {
                        var resolution = await onSaveConflict(conflictReport);

                        if (resolution == Models.ConflictResolution.Cancel)
                        {
                            game.LaunchStatusMessage = string.Empty;
                            StatusChanged?.Invoke(this, new GameStatusInfo
                            {
                                Status = GameRunStatus.Idle,
                                GameName = game.Name,
                                GameId = game.Id,
                                Message = "启动已取消（存档冲突）"
                            });
                            return false;
                        }

                        switch (resolution)
                        {
                            case Models.ConflictResolution.OverwriteExitSave:
                                // 覆盖退出存档：删除旧的退出存档，用当前存档内容重建，备份成功后继续启动
                                skipRestore = true;

                                game.LaunchStatusMessage = "正在更新退出存档...";
                                StatusChanged?.Invoke(this, new GameStatusInfo
                                {
                                    Status = GameRunStatus.Launching,
                                    GameName = game.Name,
                                    GameId = game.Id,
                                    Message = $"正在用当前存档更新 {game.Name} 的退出存档...",
                                    Progress = 55
                                });

                                await _localStorageService.CreateExitSaveAsync(game, useHotBackup: false);

                                game.LaunchStatusMessage = "退出存档已更新，正在启动游戏...";
                                StatusChanged?.Invoke(this, new GameStatusInfo
                                {
                                    Status = GameRunStatus.Launching,
                                    GameName = game.Name,
                                    GameId = game.Id,
                                    Message = $"{game.Name} 退出存档已更新，正在启动游戏...",
                                    Progress = 80
                                });
                                break;

                            case Models.ConflictResolution.SaveAsNewAndLaunch:
                                // 另存为新存档：将当前存档备份为一个新存档（保留旧退出存档），
                                // 不覆盖当前游戏存档目录，直接以当前目录内容启动
                                skipRestore = true;

                                game.LaunchStatusMessage = "正在另存为新存档...";
                                StatusChanged?.Invoke(this, new GameStatusInfo
                                {
                                    Status = GameRunStatus.Launching,
                                    GameName = game.Name,
                                    GameId = game.Id,
                                    Message = $"正在将当前存档另存为 {game.Name} 的新存档...",
                                    Progress = 55
                                });

                                await _localStorageService.BackupSaveAsync(game, ConflictSaveCopyName, "存档冲突时另存的新存档", useHotBackup: false);

                                game.LaunchStatusMessage = "新存档已保存，正在启动游戏...";
                                StatusChanged?.Invoke(this, new GameStatusInfo
                                {
                                    Status = GameRunStatus.Launching,
                                    GameName = game.Name,
                                    GameId = game.Id,
                                    Message = $"{game.Name} 新存档已保存，正在启动游戏...",
                                    Progress = 80
                                });
                                break;

                            case Models.ConflictResolution.OverwriteGameSave:
                                // 覆盖游戏存档：用退出存档覆盖游戏存档目录（放弃当前存档修改），
                                // 走下方"清空目录 + 解压退出存档"的正常恢复流程，然后启动
                                break;
                        }
                    }
                }

                if (!skipRestore)
                {
                    // 在删除任何文件之前统一预检全部目标：任一路径不安全都会直接失败，此时还未改动磁盘。
                    var restoreTargets = RestorePathSafety.ResolveRestoreTargets(
                        game.ResolvedSaveFolderPaths, _configService.WorkDirectory, latestExitSave.Path,
                        _configService.ConfigFilePath).ToList();

                    // 清空所有存档目录
                    foreach (var savePath in restoreTargets)
                    {
                        if (Directory.Exists(savePath))
                        {
                            game.LaunchStatusMessage = "正在清空存档目录...";
                            StatusChanged?.Invoke(this, new GameStatusInfo
                            {
                                Status = GameRunStatus.Launching,
                                GameName = game.Name,
                                GameId = game.Id,
                                Message = $"正在清空 {game.Name} 的存档目录...",
                                Progress = 50
                            });
                            ClearDirectory(savePath);
                        }
                        else if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                        }
                    }

                    // 解压存档
                    game.LaunchStatusMessage = "正在解压存档...";
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Launching,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = $"正在解压存档到 {game.Name} 的存档目录...",
                        Progress = 60
                    });
                    await TarHelper.ExtractTarToMultipleAsync(latestExitSave.Path, restoreTargets);

                    // 更新进度：解压完成
                    game.LaunchStatusMessage = "存档恢复完成，正在启动游戏...";
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Launching,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = $"{game.Name} 存档恢复完成，正在启动游戏...",
                        Progress = 80
                    });
                }
            }
        }

        // 2. 启动游戏进程（支持多进程顺序启动）
        game.LaunchStatusMessage = "正在启动游戏...";
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Launching,
            GameName = game.Name,
            GameId = game.Id,
            Message = $"正在启动 {game.Name}...",
            Progress = 90
        });

        var processEntries = game.AllProcessEntries;
        System.Diagnostics.Process? process = null;
        List<System.Diagnostics.Process> allProcesses = new();
        bool isSteamLaunch = game.LaunchViaSteam;
        bool steamNoMonitoring = isSteamLaunch && !game.HasProcessPath;

        try
        {
            if (isSteamLaunch)
            {
                // Steam 启动模式：通过 steam://rungameid/{appId} 协议启动
                process = _processMonitorService.LaunchViaSteamProtocol(game.SteamAppId!);
                if (process != null)
                {
                    allProcesses.Add(process);
                }

                // 如果还有配置的附加进程，按顺序启动
                if (processEntries.Count > 1)
                {
                    // 跳过主进程条目（Steam 已代替），只启动第二进程
                    var secondaryEntries = processEntries.Skip(1).ToList();
                    if (secondaryEntries.Count > 0)
                    {
                        var multiResult = await _processMonitorService.LaunchMultipleProcessesAsync(secondaryEntries);
                        if (!multiResult.Success)
                        {
                            game.LaunchStatusMessage = string.Empty;
                            StatusChanged?.Invoke(this, new GameStatusInfo
                            {
                                Status = GameRunStatus.Idle,
                                GameName = game.Name,
                                GameId = game.Id,
                                Message = multiResult.ErrorMessage ?? "启动失败"
                            });
                            LastLaunchError = multiResult.ErrorMessage;
                            ProcessLaunchFailed?.Invoke(this, multiResult.ErrorMessage ?? "启动失败");
                            return false;
                        }
                        allProcesses.AddRange(multiResult.LaunchedProcesses);
                    }
                }
            }
            else if (processEntries.Count > 1)
            {
                // 多进程模式：顺序启动，前一个成功后再启动下一个
                var multiResult = await _processMonitorService.LaunchMultipleProcessesAsync(processEntries);
                if (!multiResult.Success)
                {
                    // 启动失败，通知 UI 层弹窗
                    game.LaunchStatusMessage = string.Empty;
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Idle,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = multiResult.ErrorMessage ?? "启动失败"
                    });
                    LastLaunchError = multiResult.ErrorMessage;
                    ProcessLaunchFailed?.Invoke(this, multiResult.ErrorMessage ?? "启动失败");
                    return false;
                }
                allProcesses = multiResult.LaunchedProcesses;
                process = allProcesses.FirstOrDefault();
            }
            else
            {
                // 单进程模式：保持原有逻辑
                if (!isSteamLaunch)
                {
                    process = _processMonitorService.LaunchProcess(game.ProcessPath!, game.ProcessArgs);
                    if (process == null)
                        throw new InvalidOperationException("启动游戏进程失败");
                    allProcesses.Add(process);
                }
            }

            if (process == null && !isSteamLaunch)
                throw new InvalidOperationException("启动游戏进程失败");
        }
        catch
        {
            // 启动失败（进程路径无效、权限不足、Steam 未运行等异常情况）：
            // 先清空启动状态消息并恢复空闲状态，避免列表项启动按钮旁残留"正在启动..."提示，
            // 再重新抛出异常，由上层捕获后弹窗提示具体错误
            game.LaunchStatusMessage = string.Empty;
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.Idle,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"启动 {game.Name} 失败"
            });
            throw;
        }

        if (steamNoMonitoring)
        {
            // 无监测模式：不设 IsGameRunning，直接跳过
        }
        else
        {
            IsGameRunning = true;
        }
        RunningGameId = game.Id;

        if (isSteamLaunch)
        {
            if (steamNoMonitoring)
            {
                // 无进程路径的 Steam 启动：不设运行状态（无法监测）
                RunningProcessId = 0;
                RunningProcessIds.Clear();
                RunningProcessName = null;
                RunningProcessNames.Clear();
            }
            else
            {
                // Steam 启动模式：只记录游戏进程名，不记录 Steam 客户端 PID（避免误杀 Steam 进程）
                var gameProcessName = Path.GetFileNameWithoutExtension(game.ProcessPath) ?? string.Empty;
                RunningProcessId = 0;
                RunningProcessIds.Clear();
                RunningProcessName = gameProcessName;
                RunningProcessNames = new List<string> { gameProcessName };
                if (!string.IsNullOrWhiteSpace(game.SecondaryProcessPath))
                {
                    RunningProcessNames.Add(Path.GetFileNameWithoutExtension(game.SecondaryProcessPath));
                }
                game.IsRunning = true;
                game.RunningPid = 0;
                game.RunningPids = new List<int>();
            }
        }
        else
        {
            RunningProcessId = process!.Id;
            RunningProcessIds = allProcesses.Select(p => p.Id).ToList();
            RunningProcessName = process.ProcessName;
            RunningProcessNames = allProcesses.Select(p => p.ProcessName).ToList();
            game.IsRunning = true;
            game.RunningPid = process.Id;
            game.RunningPids = allProcesses.Select(p => p.Id).ToList();
        }
        game.LaunchStatusMessage = string.Empty; // 启动完成，清空启动状态消息

        // 启动定时备份（如果已配置）
        if (game.ScheduledBackupEnabled)
        {
            ScheduledBackup.StartScheduledBackup(game);
        }

        // 通知：游戏运行中（启动完成）
        var pidDisplay = isSteamLaunch
            ? $"Steam (App ID: {game.SteamAppId})"
            : allProcesses.Count > 1
                ? string.Join(", ", allProcesses.Select(p => p.Id))
                : process!.Id.ToString();
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Running,
            GameName = game.Name,
            GameId = game.Id,
            ProcessId = process?.Id ?? 0,
            Message = $"游戏 {game.Name} 运行中 ({pidDisplay})"
        });

        // 3. 后台监测进程退出（支持多进程/Steam启动）
        // 无进程路径的 Steam 启动跳过监测，直接清理状态
        if (steamNoMonitoring)
        {
            // 短暂延迟后恢复空闲，释放 Steam 客户端进程引用
            _ = Task.Run(async () =>
            {
                try
                {
                    // 释放 Steam 进程引用
                    foreach (var p in allProcesses)
                    {
                        try { p.Dispose(); } catch { }
                    }

                    // 短暂延迟后恢复状态
                    await Task.Delay(2000);

                    IsGameRunning = false;
                    RunningGameId = null;

                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Completed,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = $"已通过 Steam 启动 {game.Name}（未设置进程监测，退出后不会自动备份）"
                    });

                    await Task.Delay(3000);
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Idle,
                        Message = "同步就绪"
                    });
                }
                catch { }
            });

            return true;
        }

        var capturedProcesses = new List<System.Diagnostics.Process>(allProcesses);
        var capturedProcessName = Path.GetFileNameWithoutExtension(game.ProcessPath) ?? string.Empty;
        var capturedSteamAppId = game.SteamAppId;
        _ = Task.Run(async () =>
        {
            ProcessExitType exitType;
            if (isSteamLaunch)
            {
                // Steam 启动模式：使用专用的 Steam 进程等待方法
                exitType = await _processMonitorService.WaitForSteamLaunchedProcessExitAsync(
                    capturedProcessName, capturedSteamAppId ?? "");

                // 释放可能捕获的 Steam 客户端进程
                foreach (var p in capturedProcesses)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            else if (capturedProcesses.Count > 1)
            {
                // 多进程：等待所有进程退出
                exitType = await _processMonitorService.WaitForAllProcessesExitAsync(capturedProcesses);
                // Process 对象已在 WaitForAllProcessesExitAsync 内部释放
            }
            else
            {
                // 单进程：保持原有逻辑
                exitType = await _processMonitorService.WaitForExitAsync(process!);
                process!.Dispose();
            }

            IsGameRunning = false;
            RunningGameId = null;
            RunningProcessId = 0;
            RunningProcessIds.Clear();
            RunningProcessName = null;
            RunningProcessNames.Clear();

            // 停止定时备份
            ScheduledBackup.StopScheduledBackup(game.Id);

            // 使用应用主窗口的 DispatcherQueue 回到 UI 线程更新属性
            var dispatcherQueue = App.MainWindow?.DispatcherQueue;
            if (dispatcherQueue != null)
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    game.IsRunning = false;
                });
            }
            else
            {
                // Fallback：直接设置（可能无法正确通知 UI）
                game.IsRunning = false;
            }

            // 如果监测被取消，直接恢复空闲状态
            if (exitType == ProcessExitType.Cancelled)
            {
                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Idle,
                    GameName = game.Name,
                    GameId = game.Id,
                    Message = "监测已取消"
                });
                GameExited?.Invoke(this, game.Id);
                return;
            }

            // 如果检测到崩溃或未成功启动，通知 UI 并跳过备份
            if (exitType == ProcessExitType.CrashOrNotLaunched)
            {
                System.Diagnostics.Debug.WriteLine($"[进程监测] 游戏 {game.Name} 疑似崩溃或未成功启动，跳过退出备份");

                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Idle,
                    GameName = game.Name,
                    GameId = game.Id,
                    Message = $"检测到 {game.Name} 异常退出，本次不备份"
                });

                // 触发崩溃检测事件，通知 UI 层弹窗
                GameCrashDetected?.Invoke(this, game.Name);
                GameExited?.Invoke(this, game.Id);
                return;
            }

            // 正常退出：执行备份流程
            // 通知：正在备份
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.BackingUp,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"游戏 {game.Name} 已退出，正在创建存档快照并备份..."
            });

            // 进程退出后自动备份
            SaveFile? save = null;
            string? backupError = null;
            try
            {
                bool anyHasFiles = game.ResolvedSaveFolderPaths.Any(p =>
                    (Directory.Exists(p) &&
                    Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Any()) ||
                    (File.Exists(p) && !Directory.Exists(p)));
                if (anyHasFiles)
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

                    // 游戏已退出，使用直接模式打包（无需热备份）
                    save = await _localStorageService.CreateExitSaveAsync(game, progress, useHotBackup: false);
                    BackupCompleted?.Invoke(this, save);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自动备份失败: {ex.Message}");
                backupError = ex.Message;
            }

            // 备份完成后，检测是否需要上传到云端
            bool uploaded = false;
            if (save != null)
            {
                uploaded = await TryUploadToCloudAsync(game, save);
            }

            // 如果没有上传到云端，则手动发送完成/失败状态
            if (!uploaded)
            {
                if (backupError != null)
                {
                    // 备份失败 — 发送 Idle 状态（不会触发成功通知）
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Idle,
                        GameName = game.Name,
                        GameId = game.Id,
                        Message = $"游戏 {game.Name} 存档备份失败: {backupError}"
                    });

                    // 触发备份失败的通知（通过 GameCrashDetected 不合适，新增专用事件）
                    BackupFailed?.Invoke(this, (game.Name, backupError));
                }
                else
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

        // 手动备份根据游戏进程状态选择热备份或直接备份。
        bool needHotBackup = !game.HasAnyLaunchMethod || ProcessMonitorService.IsProcessRunning(game.ProcessPath);
        return await BackupCoreAsync(game, name, needHotBackup);
    }

    /// <summary>
    /// 游戏运行中通过全局快捷键创建存档，始终使用热备份模式。
    /// </summary>
    public Task<SaveFile> QuickBackupAsync(Game game, DateTimeOffset backupTime)
    {
        return BackupCoreAsync(game, CreateQuickSaveName(backupTime), useHotBackup: true);
    }

    /// <summary>
    /// 创建可读的快捷存档名称。
    /// </summary>
    public static string CreateQuickSaveName(DateTimeOffset backupTime)
    {
        return $"快捷存档 {backupTime.LocalDateTime:yyyy-MM-dd HH-mm-ss}";
    }

    private async Task<SaveFile> BackupCoreAsync(Game game, string name, bool useHotBackup)
    {

        var progress = new Progress<double>(p =>
        {
            StatusChanged?.Invoke(this, new GameStatusInfo
            {
                Status = GameRunStatus.BackingUp,
                GameName = game.Name,
                GameId = game.Id,
                Message = $"正在创建 {game.Name} 的存档快照并备份...",
                Progress = p
            });
        });

        var save = await _localStorageService.BackupSaveAsync(game, name, null, progress, useHotBackup);
        BackupCompleted?.Invoke(this, save);

        // 通知：本地备份完成
        StatusChanged?.Invoke(this, new GameStatusInfo
        {
            Status = GameRunStatus.Completed,
            GameName = game.Name,
            GameId = game.Id,
            Message = $"{game.Name} 存档备份完成"
        });

        // 手动备份完成后，后台上传到云端（不阻塞备份结果返回）
        _ = Task.Run(async () =>
        {
            try
            {
                var uploaded = await TryUploadToCloudAsync(game, save);
                // 如果没有云端上传（未配置或已禁用），手动恢复到空闲状态
                if (!uploaded)
                {
                    await Task.Delay(3000);
                    StatusChanged?.Invoke(this, new GameStatusInfo
                    {
                        Status = GameRunStatus.Idle,
                        Message = "同步就绪"
                    });
                }
                // 如果有云端上传，TryUploadToCloudAsync 内部已处理 Completed → Idle 状态转换
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[手动备份] 云端上传异常: {ex.Message}");
                // 异常时也恢复空闲状态
                StatusChanged?.Invoke(this, new GameStatusInfo
                {
                    Status = GameRunStatus.Idle,
                    Message = $"云端同步失败: {ex.Message}"
                });
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

        // 还原成功后，同步更新退出存档
        // 防止下次启动游戏时旧的退出存档覆盖还原后的内容
        // 恢复存档时游戏已确认未运行（前面有检查），使用直接模式
        await _localStorageService.CreateExitSaveAsync(game, useHotBackup: false);

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
        // 停止定时备份
        ScheduledBackup.StopScheduledBackup(game.Id);

        // 删除前先确认游戏标识是普通单段目录名，避免 ".." 或绝对路径把删除带出工作目录
        WorkDirectorySafety.ValidateGameId(game.Id);

        // 直接拼路径，不调用 GetGameWorkDirectory（那个方法会自动创建目录）
        var gameWorkDir = Path.Combine(_configService.WorkDirectory, game.Id);

        // 不接受链接工作目录，避免误操作映射到其它位置的数据。
        try { WorkDirectorySafety.EnsureNoReparsePoints(gameWorkDir); }
        catch (IOException ex)
        {
            throw new InvalidOperationException("备份目录或其上级存在符号链接/目录联接，已取消删除游戏，请先核对备份实际位置。", ex);
        }

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
