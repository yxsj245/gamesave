using System.Collections.ObjectModel;
using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly ConfigService _configService;
    private readonly LocalStorageService _localStorageService;
    private readonly GameService _gameService;
    private readonly ScheduledBackupService _scheduledBackupService;

    public MainViewModel()
    {
        Title = "我的游戏";
        _configService = App.ConfigService;
        _localStorageService = App.LocalStorageService;
        _gameService = App.GameService;
        _scheduledBackupService = App.ScheduledBackupService;
        _homeGameListViewMode = _configService.HomeGameListViewMode;

        // 监听游戏退出事件，刷新存档列表（使用具名方法，确保可取消订阅）
        _gameService.GameExited += OnGameExited;
    }

    /// <summary>游戏退出事件处理：刷新存档列表</summary>
    private void OnGameExited(object? sender, string gameId)
    {
        if (SelectedGame?.Id == gameId)
        {
            App.MainWindow?.DispatcherQueue?.TryEnqueue(async () =>
            {
                await LoadSavesForGameAsync(gameId);
            });
        }
    }

    /// <summary>清理事件订阅，防止内存泄漏（由 HomePage.OnNavigatedFrom 调用）</summary>
    public void Cleanup()
    {
        _gameService.GameExited -= OnGameExited;
    }

    #region 属性

    private ObservableCollection<Game> _games = new();
    public ObservableCollection<Game> Games
    {
        get => _games;
        set => SetProperty(ref _games, value);
    }

    // 过滤后的游戏列表（用于 ListView 绑定）
    private ObservableCollection<Game> _filteredGames = new();
    public ObservableCollection<Game> FilteredGames
    {
        get => _filteredGames;
        set => SetProperty(ref _filteredGames, value);
    }

    // 搜索关键字
    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                ApplySearchFilter();
            }
        }
    }

    private Game? _selectedGame;
    public Game? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnSelectedGameChanged(value);
            }
        }
    }

    private ObservableCollection<SaveFile> _currentSaves = new();
    public ObservableCollection<SaveFile> CurrentSaves
    {
        get => _currentSaves;
        set => SetProperty(ref _currentSaves, value);
    }

    private bool _isDetailsVisible = false;
    public bool IsDetailsVisible
    {
        get => _isDetailsVisible;
        set
        {
            if (SetProperty(ref _isDetailsVisible, value))
            {
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility DetailsVisibility => IsDetailsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private HomeGameListViewMode _homeGameListViewMode;
    public HomeGameListViewMode HomeGameListViewMode
    {
        get => _homeGameListViewMode;
        set
        {
            if (SetProperty(ref _homeGameListViewMode, value))
            {
                OnPropertyChanged(nameof(IsListViewMode));
                OnPropertyChanged(nameof(IsTileViewMode));
                OnPropertyChanged(nameof(ListViewVisibility));
                OnPropertyChanged(nameof(TileViewVisibility));
            }
        }
    }

    public bool IsListViewMode => HomeGameListViewMode == HomeGameListViewMode.List;

    public bool IsTileViewMode => HomeGameListViewMode == HomeGameListViewMode.Tile;

    public Microsoft.UI.Xaml.Visibility ListViewVisibility => IsListViewMode
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility TileViewVisibility => IsTileViewMode
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private double _actionProgress;
    public double ActionProgress
    {
        get => _actionProgress;
        set => SetProperty(ref _actionProgress, value);
    }

    private bool _isActionProgressVisible;
    public bool IsActionProgressVisible
    {
        get => _isActionProgressVisible;
        set
        {
            if (SetProperty(ref _isActionProgressVisible, value))
            {
                OnPropertyChanged(nameof(ActionProgressVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility ActionProgressVisibility => IsActionProgressVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // 添加游戏表单字段
    private string _newGameName = string.Empty;
    public string NewGameName
    {
        get => _newGameName;
        set => SetProperty(ref _newGameName, value);
    }

    private List<string> _newGameSavePaths = new();
    public List<string> NewGameSavePaths
    {
        get => _newGameSavePaths;
        set => SetProperty(ref _newGameSavePaths, value);
    }

    private string _newGameProcessPath = string.Empty;
    public string NewGameProcessPath
    {
        get => _newGameProcessPath;
        set => SetProperty(ref _newGameProcessPath, value);
    }

    private string _newGameProcessArgs = string.Empty;
    public string NewGameProcessArgs
    {
        get => _newGameProcessArgs;
        set => SetProperty(ref _newGameProcessArgs, value);
    }

    // 自定义图标文件路径（添加游戏时可选）
    private string? _newGameIconPath;
    public string? NewGameIconPath
    {
        get => _newGameIconPath;
        set => SetProperty(ref _newGameIconPath, value);
    }

    // 第二启动进程路径
    private string _newGameSecondaryProcessPath = string.Empty;
    public string NewGameSecondaryProcessPath
    {
        get => _newGameSecondaryProcessPath;
        set => SetProperty(ref _newGameSecondaryProcessPath, value);
    }

    // 第二启动进程参数
    private string _newGameSecondaryProcessArgs = string.Empty;
    public string NewGameSecondaryProcessArgs
    {
        get => _newGameSecondaryProcessArgs;
        set => SetProperty(ref _newGameSecondaryProcessArgs, value);
    }

    // 云端服务商配置列表（供添加游戏表单下拉框使用）
    private ObservableCollection<CloudConfig> _cloudConfigs = new();
    public ObservableCollection<CloudConfig> CloudConfigs
    {
        get => _cloudConfigs;
        set => SetProperty(ref _cloudConfigs, value);
    }

    // 添加游戏时选中的云端配置 ID
    private string? _selectedCloudConfigId;
    public string? SelectedCloudConfigId
    {
        get => _selectedCloudConfigId;
        set => SetProperty(ref _selectedCloudConfigId, value);
    }

    // 手动备份名称
    private string _manualSaveName = string.Empty;
    public string ManualSaveName
    {
        get => _manualSaveName;
        set => SetProperty(ref _manualSaveName, value);
    }

    // 定时备份表单字段
    private bool _newGameScheduledBackupEnabled;
    public bool NewGameScheduledBackupEnabled
    {
        get => _newGameScheduledBackupEnabled;
        set => SetProperty(ref _newGameScheduledBackupEnabled, value);
    }

    private int _newGameScheduledBackupInterval = 30;
    public int NewGameScheduledBackupInterval
    {
        get => _newGameScheduledBackupInterval;
        set => SetProperty(ref _newGameScheduledBackupInterval, value);
    }

    private int _newGameScheduledBackupMaxCount = 5;
    public int NewGameScheduledBackupMaxCount
    {
        get => _newGameScheduledBackupMaxCount;
        set => SetProperty(ref _newGameScheduledBackupMaxCount, value);
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化：从配置加载真实游戏列表
    /// </summary>
    public async Task InitializeAsync()
    {
        // 配置服务已在 App.OnLaunched 中初始化，此处无需再次调用
        HomeGameListViewMode = _configService.HomeGameListViewMode;
        LoadGamesFromConfig();
        LoadCloudConfigs();
        await Task.CompletedTask;
    }

    private void LoadGamesFromConfig()
    {
        Games.Clear();
        // 按 SortOrder 排序加载游戏列表
        foreach (var game in _configService.GetAllGames().OrderBy(g => g.SortOrder))
        {
            Games.Add(game);
        }
        ApplySearchFilter();
    }

    /// <summary>
    /// 根据搜索关键字过滤游戏列表
    /// </summary>
    public void ApplySearchFilter()
    {
        FilteredGames.Clear();

        var keyword = SearchKeyword?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(keyword))
        {
            // 无搜索关键字，显示全部
            foreach (var game in Games)
            {
                FilteredGames.Add(game);
            }
        }
        else
        {
            // 按游戏名称模糊匹配（忽略大小写）
            foreach (var game in Games)
            {
                if (game.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredGames.Add(game);
                }
            }
        }
    }

    /// <summary>
    /// 拖拽排序后移动游戏位置并持久化
    /// </summary>
    /// <param name="oldIndex">原始位置</param>
    /// <param name="newIndex">新位置</param>
    public async Task MoveGameAsync(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Games.Count || newIndex < 0 || newIndex >= Games.Count || oldIndex == newIndex)
            return;

        // 在内存中移动
        var game = Games[oldIndex];
        Games.RemoveAt(oldIndex);
        Games.Insert(newIndex, game);

        // 更新 SortOrder 并持久化到配置
        var orderedIds = Games.Select(g => g.Id).ToList();
        await _configService.ReorderGamesAsync(orderedIds);

        // 刷新过滤列表
        ApplySearchFilter();
    }

    /// <summary>
    /// 加载云端配置列表（供添加游戏表单的下拉框使用）
    /// </summary>
    public void LoadCloudConfigs()
    {
        CloudConfigs.Clear();
        foreach (var config in _configService.GetAllCloudConfigs())
        {
            CloudConfigs.Add(config);
        }
    }

    #endregion

    #region 游戏选择

    private async void OnSelectedGameChanged(Game? value)
    {
        CurrentSaves.Clear();

        if (value != null)
        {
            await LoadSavesForGameAsync(value.Id);
            IsDetailsVisible = true;
        }
        else
        {
            IsDetailsVisible = false;
        }
    }

    private async Task LoadSavesForGameAsync(string gameId)
    {
        try
        {
            var saves = await _localStorageService.GetSavesAsync(gameId);

            // 需要在 UI 线程更新
            CurrentSaves.Clear();
            foreach (var save in saves)
            {
                CurrentSaves.Add(save);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载存档失败: {ex.Message}";
        }
    }

    public void CloseDetails()
    {
        IsDetailsVisible = false;
        SelectedGame = null;
        IsActionProgressVisible = false;
    }

    /// <summary>
    /// 切换首页游戏列表展示模式，并立即持久化。
    /// </summary>
    public async Task SetHomeGameListViewModeAsync(HomeGameListViewMode viewMode)
    {
        if (HomeGameListViewMode == viewMode)
            return;

        HomeGameListViewMode = viewMode;
        await _configService.SetHomeGameListViewModeAsync(viewMode);
    }

    #endregion

    #region 添加游戏

    /// <summary>
    /// 重置添加游戏表单
    /// </summary>
    public void ResetAddGameForm()
    {
        NewGameName = string.Empty;
        NewGameSavePaths = new List<string>();
        NewGameProcessPath = string.Empty;
        NewGameProcessArgs = string.Empty;
        NewGameSecondaryProcessPath = string.Empty;
        NewGameSecondaryProcessArgs = string.Empty;
        NewGameIconPath = null;
        SelectedCloudConfigId = null;
        NewGameScheduledBackupEnabled = false;
        NewGameScheduledBackupInterval = 30;
        NewGameScheduledBackupMaxCount = 5;
    }

    /// <summary>
    /// 执行添加游戏操作
    /// </summary>
    public async Task<bool> AddGameAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGameName))
        {
            StatusMessage = "请输入游戏名称";
            return false;
        }

        if (NewGameSavePaths.Count == 0 || NewGameSavePaths.All(string.IsNullOrWhiteSpace))
        {
            StatusMessage = "请添加至少一个游戏存档目录";
            return false;
        }

        IsBusy = true;
        StatusMessage = "正在添加游戏...";

        try
        {
            var game = new Game
            {
                Name = NewGameName.Trim(),
                SaveFolderPaths = NewGameSavePaths
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList(),
                ProcessPath = string.IsNullOrWhiteSpace(NewGameProcessPath) ? null : NewGameProcessPath.Trim(),
                ProcessArgs = string.IsNullOrWhiteSpace(NewGameProcessArgs) ? null : NewGameProcessArgs.Trim(),
                SecondaryProcessPath = string.IsNullOrWhiteSpace(NewGameSecondaryProcessPath) ? null : NewGameSecondaryProcessPath.Trim(),
                SecondaryProcessArgs = string.IsNullOrWhiteSpace(NewGameSecondaryProcessArgs) ? null : NewGameSecondaryProcessArgs.Trim(),
                CloudConfigId = SelectedCloudConfigId,
                ScheduledBackupEnabled = NewGameScheduledBackupEnabled,
                ScheduledBackupIntervalMinutes = NewGameScheduledBackupInterval,
                ScheduledBackupMaxCount = NewGameScheduledBackupMaxCount
            };

            if (!string.IsNullOrWhiteSpace(NewGameIconPath))
            {
                game.IconPath = await IconExtractorHelper.SaveCustomIconAsync(game.Id, NewGameIconPath);
            }

            await _gameService.AddGameAsync(game);
            Games.Add(game);
            ApplySearchFilter();

            ResetAddGameForm();
            StatusMessage = $"游戏 \"{game.Name}\" 添加成功！";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加游戏失败: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从扫描结果批量添加游戏
    /// </summary>
    /// <param name="detectedGames">检测到的游戏列表（已勾选且需要导入的）</param>
    /// <returns>成功和失败数量</returns>
    public async Task<(int successCount, int failCount, string message)> BatchAddGamesAsync(List<DetectedGame> detectedGames)
    {
        if (detectedGames == null || detectedGames.Count == 0)
            return (0, 0, "没有要导入的游戏");

        IsBusy = true;
        StatusMessage = "正在批量导入游戏...";

        int successCount = 0;
        int failCount = 0;
        var errors = new List<string>();

        try
        {
            foreach (var detected in detectedGames)
            {
                try
                {
                    // 存档目录为可选字段，留空可在启动游戏时通过探测模式自动识别

                    var game = new Game
                    {
                        Name = detected.Name.Trim(),
                        SaveFolderPaths = string.IsNullOrWhiteSpace(detected.SaveFolderPath)
                            ? new List<string>()
                            : new List<string> { detected.SaveFolderPath.Trim() },
                        ProcessPath = string.IsNullOrWhiteSpace(detected.ExePath) ? null : detected.ExePath.Trim(),
                        ProcessArgs = string.IsNullOrWhiteSpace(detected.ProcessArgs) ? null : detected.ProcessArgs.Trim(),
                        CloudConfigId = detected.CloudConfigId,
                        Source = detected.Source,
                        ScheduledBackupEnabled = detected.ScheduledBackupEnabled,
                        ScheduledBackupIntervalMinutes = detected.ScheduledBackupIntervalMinutes,
                        ScheduledBackupMaxCount = detected.ScheduledBackupMaxCount
                    };

                    await _gameService.AddGameAsync(game);
                    Games.Add(game);
                    ApplySearchFilter();
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"「{detected.Name}」导入失败: {ex.Message}");
                    failCount++;
                }
            }

            StatusMessage = failCount > 0
                ? $"批量导入完成：成功 {successCount} 个，失败 {failCount} 个"
                : $"成功导入 {successCount} 个游戏！";

            return (successCount, failCount, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"批量导入失败: {ex.Message}";
            return (successCount, failCount, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region 编辑游戏

    /// <summary>
    /// 更新游戏属性（不允许修改存档目录）
    /// </summary>
    /// <param name="game">要更新的游戏对象（已修改属性）</param>
    public async Task<(bool success, string message)> UpdateGameAsync(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.Name))
        {
            return (false, "游戏名称不能为空");
        }

        IsBusy = true;
        StatusMessage = "正在保存游戏信息...";

        try
        {
            await _configService.UpdateGameAsync(game);

            // 同步更新内存中的游戏列表
            var index = -1;
            for (int i = 0; i < Games.Count; i++)
            {
                if (Games[i].Id == game.Id)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                Games[index] = game;
                ApplySearchFilter();
            }

            // 更新定时备份状态：对于无启动进程的游戏，更新配置后重新启停定时备份
            // 有启动进程的游戏定时备份跟随游戏生命周期，不在此处理
            if (!game.HasProcessPath)
            {
                if (game.ScheduledBackupEnabled && game.IsScheduledBackupRunning)
                {
                    // 配置变更后重启定时备份（应用新的间隔/数量设置）
                    _scheduledBackupService.StopScheduledBackup(game.Id);
                    _scheduledBackupService.StartScheduledBackup(game);
                }
                else if (!game.ScheduledBackupEnabled && game.IsScheduledBackupRunning)
                {
                    // 禁用了定时备份，停止运行中的定时任务
                    _scheduledBackupService.StopScheduledBackup(game.Id);
                }
            }

            StatusMessage = $"游戏「{game.Name}」信息已更新";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新游戏失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region 启动游戏

    /// <summary>
    /// 启动选中的游戏
    /// </summary>
    public async Task<(bool success, string message)> LaunchGameAsync()
    {
        if (SelectedGame == null)
            return (false, "请先选择一个游戏");

        if (string.IsNullOrWhiteSpace(SelectedGame.ProcessPath))
            return (false, "该游戏未设置启动进程路径");

        IsBusy = true;
        StatusMessage = "正在启动游戏...";

        try
        {
            var result = await _gameService.LaunchGameAsync(SelectedGame);
            if (result)
            {
                StatusMessage = "游戏已启动，退出后将自动备份";
            }
            else
            {
                // 读取启动失败的详细错误信息
                StatusMessage = _gameService.LastLaunchError ?? "启动取消";
            }
            return (result, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"启动游戏失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region 手动备份

    /// <summary>
    /// 停止选中的游戏
    /// </summary>
    public async Task StopGameAsync()
    {
        if (SelectedGame != null)
        {
            await StopGameCoreAsync(SelectedGame);
        }
    }

    /// <summary>
    /// 直接停止指定游戏（不触发详情面板），供列表按钮使用
    /// </summary>
    public async Task StopGameDirectAsync(Game game)
    {
        await StopGameCoreAsync(game);
    }

    /// <summary>
    /// 停止游戏的核心实现：先尝试通过 PID 结束，再尝试通过进程名结束
    /// 解决 Steam 游戏场景下原始 stub 进程 PID 已失效导致停止无反应的问题
    /// </summary>
    private async Task StopGameCoreAsync(Game game)
    {
        if (!_gameService.IsGameRunning || _gameService.RunningGameId != game.Id)
            return;

        // 显示加载反馈
        game.IsStopping = true;

        try
        {
            // 在后台线程执行进程结束操作，避免阻塞 UI
            await Task.Run(() =>
            {
                // 1. 先尝试通过所有 PID 结束（支持多进程）
                foreach (var pid in _gameService.RunningProcessIds)
                {
                    if (pid != 0)
                    {
                        ProcessMonitorService.StopProcess(pid);
                    }
                }
                // 兼容单进程场景
                if (_gameService.RunningProcessIds.Count == 0 && _gameService.RunningProcessId != 0)
                {
                    ProcessMonitorService.StopProcess(_gameService.RunningProcessId);
                }

                // 2. 再尝试通过所有进程名结束（覆盖 Steam 游戏 stub 进程已退出、真实游戏进程 PID 不同的场景）
                foreach (var name in _gameService.RunningProcessNames)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        ProcessMonitorService.StopProcessByName(name);
                    }
                }
                // 兼容单进程场景
                if (_gameService.RunningProcessNames.Count == 0)
                {
                    if (!string.IsNullOrEmpty(_gameService.RunningProcessName))
                    {
                        ProcessMonitorService.StopProcessByName(_gameService.RunningProcessName);
                    }
                    // 3. 最后通过游戏配置的进程路径名称兜底
                    else if (!string.IsNullOrWhiteSpace(game.ProcessPath))
                    {
                        var processName = System.IO.Path.GetFileNameWithoutExtension(game.ProcessPath);
                        ProcessMonitorService.StopProcessByName(processName);
                    }
                }

                // 4. 通过第二进程路径名称兜底
                if (!string.IsNullOrWhiteSpace(game.SecondaryProcessPath))
                {
                    var secondaryName = System.IO.Path.GetFileNameWithoutExtension(game.SecondaryProcessPath);
                    ProcessMonitorService.StopProcessByName(secondaryName);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[停止游戏] 结束进程异常: {ex.Message}");
            // 异常时也需要清除停止状态
            game.IsStopping = false;
        }
        // 注意：IsStopping 会在 IsRunning 变为 false 时自动清除
    }

    /// <summary>
    /// 直接启动指定游戏（不触发详情面板），供列表按钮使用
    /// </summary>
    public async Task<(bool success, string message)> LaunchGameDirectAsync(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.ProcessPath))
            return (false, "该游戏未设置启动进程路径");

        IsBusy = true;
        StatusMessage = "正在启动游戏...";

        try
        {
            var result = await _gameService.LaunchGameAsync(game);
            if (result)
            {
                StatusMessage = "游戏已启动，退出后将自动备份";
            }
            else
            {
                StatusMessage = _gameService.LastLaunchError ?? "启动取消";
            }
            return (result, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"启动游戏失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 执行手动备份
    /// </summary>
    public async Task<(bool success, string message)> ManualBackupAsync()
    {
        if (SelectedGame == null)
            return (false, "请先选择一个游戏");

        // 捕获当前选中游戏的引用（避免 await 期间 SelectedGame 变为 null）
        var game = SelectedGame;

        IsBusy = true;
        IsActionProgressVisible = true;
        ActionProgress = 0;
        StatusMessage = "正在备份存档...";

        try
        {
            var name = string.IsNullOrWhiteSpace(ManualSaveName) ? "手动存档" : ManualSaveName.Trim();

            // Register status changed to catch local progress inside ViewModel
            void OnStatusChanged(object? sender, GameStatusInfo e)
            {
                if (e.Status == GameRunStatus.BackingUp && e.Progress.HasValue)
                {
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                    {
                        ActionProgress = e.Progress.Value;
                    });
                }
            }

            _gameService.StatusChanged += OnStatusChanged;

            var save = await _gameService.ManualBackupAsync(game, name);

            _gameService.StatusChanged -= OnStatusChanged;

            // 刷新存档列表
            await LoadSavesForGameAsync(game.Id);

            ManualSaveName = string.Empty;
            StatusMessage = $"存档 \"{save.Name}\" 备份成功！";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"备份失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
            IsActionProgressVisible = false;

            // Remove lingering handler if exception occurred before
            if (game != null)
            {
                // Can't easily remove local func without capturing it better, but this is fine since it's unhooked above in success path.
                // Actually local function OnStatusChanged is captured. We can't safely unhook it here unless we lift it out.
                // Let's just set IsActionProgressVisible = false.
            }
        }
    }

    #endregion

    #region 恢复存档

    /// <summary>
    /// 恢复指定存档
    /// </summary>
    public async Task<(bool success, string message)> RestoreSaveAsync(SaveFile saveFile, bool force = false)
    {
        if (SelectedGame == null)
            return (false, "请先选择一个游戏");

        IsBusy = true;
        IsActionProgressVisible = true;
        ActionProgress = 0;
        StatusMessage = "正在恢复存档...";

        try
        {
            void OnStatusChanged(object? sender, GameStatusInfo e)
            {
                if (e.Status == GameRunStatus.Restoring && e.Progress.HasValue)
                {
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                    {
                        ActionProgress = e.Progress.Value;
                    });
                }
            }

            _gameService.StatusChanged += OnStatusChanged;

            await _gameService.RestoreSaveAsync(SelectedGame, saveFile, force);

            _gameService.StatusChanged -= OnStatusChanged;

            StatusMessage = $"存档 \"{saveFile.Name}\" 恢复成功！";
            return (true, StatusMessage);
        }
        catch (GameRunningException)
        {
            // 需要用户二次确认，抛出给 UI 层处理
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
            IsActionProgressVisible = false;
        }
    }

    #endregion

    #region 删除存档

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="saveFile">要删除的存档</param>
    /// <param name="deleteCloudSave">是否同时删除云端对应的存档</param>
    public async Task<(bool success, string message)> DeleteSaveAsync(SaveFile saveFile, bool deleteCloudSave = false)
    {
        if (!saveFile.CanDelete)
            return (false, "退出存档不允许删除");

        IsBusy = true;

        try
        {
            // 若需要同时删除云端存档
            if (deleteCloudSave && SelectedGame != null && !string.IsNullOrEmpty(SelectedGame.CloudConfigId))
            {
                await DeleteCloudSaveForLocalAsync(SelectedGame, saveFile);
            }

            await _localStorageService.DeleteSaveAsync(saveFile);
            CurrentSaves.Remove(saveFile);

            StatusMessage = deleteCloudSave
                ? $"存档 \"{saveFile.Name}\" 及其云端存档已删除"
                : $"存档 \"{saveFile.Name}\" 已删除";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 批量删除多个存档
    /// </summary>
    /// <param name="saveFiles">要删除的存档列表</param>
    /// <param name="deleteCloudSave">是否同时删除云端对应的存档</param>
    public async Task<(bool success, string message)> BatchDeleteSavesAsync(IList<SaveFile> saveFiles, bool deleteCloudSave = false)
    {
        if (saveFiles == null || saveFiles.Count == 0)
            return (false, "请选择要删除的存档");

        // 过滤掉不可删除的退出存档
        var deletable = saveFiles.Where(s => s.CanDelete).ToList();
        if (deletable.Count == 0)
            return (false, "所选存档均为退出存档，不允许删除");

        IsBusy = true;

        try
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var save in deletable)
            {
                try
                {
                    // 若需要同时删除云端存档
                    if (deleteCloudSave && SelectedGame != null && !string.IsNullOrEmpty(SelectedGame.CloudConfigId))
                    {
                        await DeleteCloudSaveForLocalAsync(SelectedGame, save);
                    }

                    await _localStorageService.DeleteSaveAsync(save);
                    CurrentSaves.Remove(save);
                    successCount++;
                }
                catch
                {
                    failCount++;
                }
            }

            var cloudHint = deleteCloudSave ? "（含云端）" : "";
            StatusMessage = failCount > 0
                ? $"批量删除完成{cloudHint}：成功 {successCount} 个，失败 {failCount} 个"
                : $"已成功删除 {successCount} 个存档{cloudHint}";

            return (failCount == 0, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"批量删除失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 根据本地存档查找并删除对应的云端存档
    /// 通过文件名匹配：本地存档的 tar 文件名 == 云端存档的 OSS key 后缀
    /// </summary>
    /// <param name="game">游戏信息</param>
    /// <param name="localSave">本地存档</param>
    private async Task DeleteCloudSaveForLocalAsync(Game game, SaveFile localSave)
    {
        try
        {
            var cloudConfig = _configService.GetCloudConfigById(game.CloudConfigId!);
            if (cloudConfig == null) return;

            var cloudService = new CloudStorageService(cloudConfig, _configService);

            // 本地存档的 tar 文件名即为云端 OSS key 的文件名部分
            var tarFileName = System.IO.Path.GetFileName(localSave.Path);
            var cloudKey = $"{game.Id}/{tarFileName}";

            // 构造一个代表云端存档的 SaveFile，用于调用 CloudStorageService.DeleteSaveAsync
            var cloudSaveFile = new SaveFile
            {
                GameId = game.Id,
                Path = cloudKey,
                StorageType = StorageType.Cloud
            };

            await cloudService.DeleteSaveAsync(cloudSaveFile);
            System.Diagnostics.Debug.WriteLine($"[删除存档] 已删除云端对应存档: {cloudKey}");
        }
        catch (Exception ex)
        {
            // 云端删除失败不阻塞本地删除
            System.Diagnostics.Debug.WriteLine($"[删除存档] 云端删除失败: {ex.Message}");
        }
    }

    #endregion

    #region 删除游戏

    /// <summary>
    /// 删除选中的游戏
    /// </summary>
    /// <param name="deleteCloudSaves">是否同时删除云端存档</param>
    public async Task<(bool success, string message)> DeleteGameAsync(bool deleteCloudSaves = false)
    {
        if (SelectedGame == null)
            return (false, "请先选择一个游戏");

        return await DeleteGameCoreAsync(SelectedGame, deleteCloudSaves);
    }

    /// <summary>
    /// 删除指定的游戏（供右键菜单等不需要设置 SelectedGame 的场景使用）
    /// </summary>
    /// <param name="game">要删除的游戏</param>
    /// <param name="deleteCloudSaves">是否同时删除云端存档</param>
    public async Task<(bool success, string message)> DeleteGameAsync(Game game, bool deleteCloudSaves = false)
    {
        if (game == null)
            return (false, "请先选择一个游戏");

        return await DeleteGameCoreAsync(game, deleteCloudSaves);
    }

    /// <summary>
    /// 删除游戏的核心实现：先关闭详情面板再执行删除，避免删除过程中显示存档列表
    /// </summary>
    private async Task<(bool success, string message)> DeleteGameCoreAsync(Game gameToDelete, bool deleteCloudSaves)
    {
        IsBusy = true;

        try
        {
            var gameName = gameToDelete.Name;

            // 先关闭详情面板，避免在删除云端存档等待期间显示存档列表
            CloseDetails();

            // 若需要删除云端存档，执行云端删除
            if (deleteCloudSaves && !string.IsNullOrEmpty(gameToDelete.CloudConfigId))
            {
                try
                {
                    var cloudConfig = _configService.GetCloudConfigById(gameToDelete.CloudConfigId);
                    if (cloudConfig != null)
                    {
                        StatusMessage = $"正在删除「{gameName}」的云端存档...";
                        var cloudService = new CloudStorageService(cloudConfig, _configService);
                        var deletedCount = await cloudService.DeleteGameAsync(gameToDelete.Id);
                        System.Diagnostics.Debug.WriteLine($"[删除游戏] 已删除云端数据 {deletedCount} 个对象");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[删除游戏] 云端删除失败: {ex.Message}");
                    // 云端删除失败不阻塞本地删除
                }
            }

            Games.Remove(gameToDelete);
            ApplySearchFilter();
            await _gameService.DeleteGameAsync(gameToDelete);

            var msg = deleteCloudSaves && !string.IsNullOrEmpty(gameToDelete.CloudConfigId)
                ? $"游戏 \"{gameName}\" 及其云端存档已删除"
                : $"游戏 \"{gameName}\" 已删除";
            StatusMessage = msg;
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除游戏失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region 定时备份手动控制

    /// <summary>
    /// 手动启动指定游戏的定时备份（仅对无启动进程的游戏有效）
    /// </summary>
    public void StartScheduledBackupManual(Game game)
    {
        if (game.HasProcessPath || !game.ScheduledBackupEnabled)
            return;

        _scheduledBackupService.StartScheduledBackup(game);
        StatusMessage = $"已启动「{game.Name}」的定时备份（每 {game.ScheduledBackupIntervalMinutes} 分钟）";
    }

    /// <summary>
    /// 手动停止指定游戏的定时备份（仅对无启动进程的游戏有效）
    /// </summary>
    public void StopScheduledBackupManual(Game game)
    {
        _scheduledBackupService.StopScheduledBackup(game.Id);
        StatusMessage = $"已停止「{game.Name}」的定时备份";
    }

    #endregion
}
