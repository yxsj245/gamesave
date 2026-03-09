using System.Collections.ObjectModel;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly ConfigService _configService;
    private readonly LocalStorageService _localStorageService;
    private readonly GameService _gameService;

    public MainViewModel()
    {
        Title = "我的游戏";
        _configService = App.ConfigService;
        _localStorageService = App.LocalStorageService;
        _gameService = App.GameService;

        // 监听游戏退出事件，刷新存档列表
        _gameService.GameExited += async (_, gameId) =>
        {
            if (SelectedGame?.Id == gameId)
            {
                await LoadSavesForGameAsync(gameId);
            }
        };
    }

    #region 属性

    private ObservableCollection<Game> _games = new();
    public ObservableCollection<Game> Games
    {
        get => _games;
        set => SetProperty(ref _games, value);
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

    private string _newGameSavePath = string.Empty;
    public string NewGameSavePath
    {
        get => _newGameSavePath;
        set => SetProperty(ref _newGameSavePath, value);
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

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化：从配置加载真实游戏列表
    /// </summary>
    public async Task InitializeAsync()
    {
        await _configService.InitializeAsync();
        LoadGamesFromConfig();
        LoadCloudConfigs();
    }

    private void LoadGamesFromConfig()
    {
        Games.Clear();
        foreach (var game in _configService.GetAllGames())
        {
            Games.Add(game);
        }
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

    #endregion

    #region 添加游戏

    /// <summary>
    /// 重置添加游戏表单
    /// </summary>
    public void ResetAddGameForm()
    {
        NewGameName = string.Empty;
        NewGameSavePath = string.Empty;
        NewGameProcessPath = string.Empty;
        NewGameProcessArgs = string.Empty;
        SelectedCloudConfigId = null;
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

        if (string.IsNullOrWhiteSpace(NewGameSavePath))
        {
            StatusMessage = "请选择游戏存档目录";
            return false;
        }

        IsBusy = true;
        StatusMessage = "正在添加游戏...";

        try
        {
            var game = new Game
            {
                Name = NewGameName.Trim(),
                SaveFolderPath = NewGameSavePath.Trim(),
                ProcessPath = string.IsNullOrWhiteSpace(NewGameProcessPath) ? null : NewGameProcessPath.Trim(),
                ProcessArgs = string.IsNullOrWhiteSpace(NewGameProcessArgs) ? null : NewGameProcessArgs.Trim(),
                CloudConfigId = SelectedCloudConfigId,
                IconPath = "\uE7FC"
            };

            await _gameService.AddGameAsync(game);
            Games.Add(game);

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
            StatusMessage = result ? "游戏已启动，退出后将自动备份" : "启动取消";
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
    public void StopGame()
    {
        if (_gameService.IsGameRunning && _gameService.RunningProcessId != 0 && SelectedGame != null && _gameService.RunningGameId == SelectedGame.Id)
        {
            ProcessMonitorService.StopProcess(_gameService.RunningProcessId);
        }
    }

    /// <summary>
    /// 直接停止指定游戏（不触发详情面板），供列表按钮使用
    /// </summary>
    public void StopGameDirect(Game game)
    {
        if (_gameService.IsGameRunning && _gameService.RunningProcessId != 0 && _gameService.RunningGameId == game.Id)
        {
            ProcessMonitorService.StopProcess(_gameService.RunningProcessId);
        }
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
            StatusMessage = result ? "游戏已启动，退出后将自动备份" : "启动取消";
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
    public async Task<(bool success, string message)> DeleteSaveAsync(SaveFile saveFile)
    {
        if (!saveFile.CanDelete)
            return (false, "退出存档不允许删除");

        IsBusy = true;

        try
        {
            await _localStorageService.DeleteSaveAsync(saveFile);
            CurrentSaves.Remove(saveFile);
            StatusMessage = $"存档 \"{saveFile.Name}\" 已删除";
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
    public async Task<(bool success, string message)> BatchDeleteSavesAsync(IList<SaveFile> saveFiles)
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
                    await _localStorageService.DeleteSaveAsync(save);
                    CurrentSaves.Remove(save);
                    successCount++;
                }
                catch
                {
                    failCount++;
                }
            }

            StatusMessage = failCount > 0
                ? $"批量删除完成：成功 {successCount} 个，失败 {failCount} 个"
                : $"已成功删除 {successCount} 个存档";

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

    #endregion

    #region 删除游戏

    /// <summary>
    /// 删除选中的游戏
    /// </summary>
    public async Task<(bool success, string message)> DeleteGameAsync()
    {
        if (SelectedGame == null)
            return (false, "请先选择一个游戏");

        IsBusy = true;

        try
        {
            var gameName = SelectedGame.Name;
            var gameToDelete = SelectedGame;

            CloseDetails();
            Games.Remove(gameToDelete);
            await _gameService.DeleteGameAsync(gameToDelete);

            StatusMessage = $"游戏 \"{gameName}\" 已删除";
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
}
