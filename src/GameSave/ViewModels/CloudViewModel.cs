using System.Collections.ObjectModel;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.ViewModels;

/// <summary>
/// 云端存档分组模型 — 按游戏分组的云端存档
/// </summary>
public class CloudSaveGroup
{
    /// <summary>游戏 ID</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>游戏名称（优先使用本地 → 云端元数据 → ID 兜底）</summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>本地是否存在该游戏配置</summary>
    public bool IsLocalGameExists { get; set; } = true;

    /// <summary>云端保存的游戏元数据（来自 game.json）</summary>
    public Game? CloudGameMetadata { get; set; }

    /// <summary>该游戏下的云端存档列表</summary>
    public ObservableCollection<SaveFile> Saves { get; set; } = new();

    /// <summary>显示用的存档数量</summary>
    public string DisplayCount => $"{Saves.Count} 个存档";
}

/// <summary>
/// 云端存档页面 ViewModel
/// </summary>
public partial class CloudViewModel : BaseViewModel
{
    private readonly ConfigService _configService;

    public CloudViewModel()
    {
        Title = "云端存档";
        _configService = App.ConfigService;
    }

    #region 属性

    private ObservableCollection<CloudConfig> _cloudConfigs = new();
    /// <summary>已配置的云端服务商列表</summary>
    public ObservableCollection<CloudConfig> CloudConfigs
    {
        get => _cloudConfigs;
        set => SetProperty(ref _cloudConfigs, value);
    }

    private CloudConfig? _selectedCloudConfig;
    /// <summary>当前选中的云端配置</summary>
    public CloudConfig? SelectedCloudConfig
    {
        get => _selectedCloudConfig;
        set
        {
            if (SetProperty(ref _selectedCloudConfig, value))
            {
                OnPropertyChanged(nameof(HasSelectedConfig));
                OnPropertyChanged(nameof(NoConfigVisibility));
                OnPropertyChanged(nameof(ContentVisibility));
            }
        }
    }

    private ObservableCollection<CloudSaveGroup> _saveGroups = new();
    /// <summary>云端存档分组列表</summary>
    public ObservableCollection<CloudSaveGroup> SaveGroups
    {
        get => _saveGroups;
        set => SetProperty(ref _saveGroups, value);
    }

    private bool _isLoading;
    /// <summary>是否正在加载</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = string.Empty;
    /// <summary>状态消息</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private double _downloadProgress;
    /// <summary>下载进度</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    /// <summary>是否已选择云端配置</summary>
    public bool HasSelectedConfig => SelectedCloudConfig != null;

    /// <summary>无配置时显示引导</summary>
    public Microsoft.UI.Xaml.Visibility NoConfigVisibility =>
        CloudConfigs.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>有配置时显示内容</summary>
    public Microsoft.UI.Xaml.Visibility ContentVisibility =>
        CloudConfigs.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    #endregion

    #region 初始化

    /// <summary>
    /// 加载云端配置列表
    /// </summary>
    public void LoadCloudConfigs()
    {
        CloudConfigs.Clear();
        foreach (var config in _configService.GetAllCloudConfigs())
        {
            CloudConfigs.Add(config);
        }

        OnPropertyChanged(nameof(NoConfigVisibility));
        OnPropertyChanged(nameof(ContentVisibility));

        // 自动选择第一个
        if (CloudConfigs.Count > 0 && SelectedCloudConfig == null)
        {
            SelectedCloudConfig = CloudConfigs[0];
        }
    }

    #endregion

    #region 刷新云端存档

    /// <summary>
    /// 刷新云端存档列表
    /// </summary>
    public async Task RefreshAsync()
    {
        if (SelectedCloudConfig == null)
        {
            StatusMessage = "请先选择一个云端配置";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在加载云端存档...";
        SaveGroups.Clear();

        try
        {
            var cloudService = new CloudStorageService(SelectedCloudConfig, _configService);
            var groupInfos = await cloudService.GetAllSavesGroupedAsync();

            foreach (var groupInfo in groupInfos)
            {
                // 尝试从本地配置找到游戏
                var localGame = _configService.GetGameById(groupInfo.GameId);
                var isLocalExists = localGame != null;

                // 游戏名称优先级：本地配置 > 云端元数据 > ID 兜底
                string gameName;
                if (localGame != null)
                {
                    gameName = localGame.Name;
                }
                else if (groupInfo.CloudGameMetadata != null)
                {
                    gameName = groupInfo.CloudGameMetadata.Name;
                }
                else
                {
                    gameName = $"未知游戏 ({groupInfo.GameId[..Math.Min(8, groupInfo.GameId.Length)]})";
                }

                var group = new CloudSaveGroup
                {
                    GameId = groupInfo.GameId,
                    GameName = gameName,
                    IsLocalGameExists = isLocalExists,
                    CloudGameMetadata = groupInfo.CloudGameMetadata
                };

                foreach (var save in groupInfo.Saves)
                {
                    group.Saves.Add(save);
                }

                SaveGroups.Add(group);
            }

            StatusMessage = SaveGroups.Count > 0
                ? $"已加载 {SaveGroups.Count} 个游戏的云端存档"
                : "云端暂无存档";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载云端存档失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region 导入恢复

    /// <summary>
    /// 从云端元数据导入游戏到本地配置
    /// </summary>
    public async Task<(bool success, string message)> ImportGameFromCloudAsync(CloudSaveGroup group)
    {
        if (group.CloudGameMetadata == null)
            return (false, "云端不包含该游戏的元数据信息，无法自动导入");

        if (_configService.GetGameById(group.GameId) != null)
            return (false, "该游戏已存在于本地");

        try
        {
            IsLoading = true;
            StatusMessage = $"正在导入游戏「{group.CloudGameMetadata.Name}」...";

            // 使用云端元数据创建本地游戏配置
            var game = new Game
            {
                Id = group.CloudGameMetadata.Id,
                Name = group.CloudGameMetadata.Name,
                SaveFolderPath = group.CloudGameMetadata.SaveFolderPath,
                IconPath = group.CloudGameMetadata.IconPath,
                ProcessPath = group.CloudGameMetadata.ProcessPath,
                ProcessArgs = group.CloudGameMetadata.ProcessArgs,
                CloudConfigId = SelectedCloudConfig?.Id, // 关联当前选中的云端配置
                AddedAt = DateTime.Now,
                Notes = group.CloudGameMetadata.Notes
            };

            // 确保游戏工作目录存在
            _configService.GetGameWorkDirectory(game.Id);

            await _configService.AddGameAsync(game);

            // 更新分组状态
            group.IsLocalGameExists = true;
            group.GameName = game.Name;

            StatusMessage = $"游戏「{game.Name}」已成功导入";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 导入游戏（如需要）并恢复指定的云端存档
    /// </summary>
    public async Task<(bool success, string message)> ImportAndRestoreAsync(CloudSaveGroup group, SaveFile cloudSave)
    {
        // 先确保游戏已导入
        if (!group.IsLocalGameExists)
        {
            var (importSuccess, importMsg) = await ImportGameFromCloudAsync(group);
            if (!importSuccess)
                return (false, importMsg);
        }

        // 下载云端存档
        var (downloadSuccess, downloadMsg) = await DownloadSaveAsync(cloudSave);
        if (!downloadSuccess)
            return (false, downloadMsg);

        // 恢复存档到本地游戏目录
        try
        {
            var game = _configService.GetGameById(group.GameId);
            if (game == null)
                return (false, "导入后未找到游戏配置");

            var localStorage = new LocalStorageService(_configService);
            var localDir = _configService.GetGameWorkDirectory(game.Id);
            var tarFileName = Path.GetFileName(cloudSave.Path);
            var localTarPath = Path.Combine(localDir, tarFileName);

            // 创建本地 SaveFile 用于恢复
            var localSave = new SaveFile
            {
                Id = cloudSave.Id,
                GameId = cloudSave.GameId,
                Name = cloudSave.Name,
                Path = localTarPath,
                BackupTime = cloudSave.BackupTime,
                SizeBytes = cloudSave.SizeBytes,
                StorageType = StorageType.Local,
                Tag = cloudSave.Tag
            };

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusMessage = $"正在将存档解压并恢复到本地: {p:F1}%";
            });

            await localStorage.RestoreSaveAsync(localSave, progress);
            StatusMessage = $"存档「{cloudSave.Name}」已恢复到本地";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复失败: {ex.Message}";
            return (false, StatusMessage);
        }
    }

    #endregion

    #region 下载云端存档

    /// <summary>
    /// 下载云端存档到本地
    /// </summary>
    public async Task<(bool success, string message)> DownloadSaveAsync(SaveFile cloudSave)
    {
        if (SelectedCloudConfig == null)
            return (false, "未选择云端配置");

        IsLoading = true;
        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessage = $"正在下载存档 \"{cloudSave.Name}\"...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusMessage = $"正在下载: {p:F1}%";
            });

            var cloudService = new CloudStorageService(SelectedCloudConfig, _configService);
            var localPath = await cloudService.DownloadSaveToLocalAsync(cloudSave, progress);
            StatusMessage = $"存档已下载到本地: {Path.GetFileName(localPath)}";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"下载失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsLoading = false;
            IsDownloading = false;
        }
    }

    #endregion

    #region 删除云端存档

    /// <summary>
    /// 删除云端存档
    /// </summary>
    public async Task<(bool success, string message)> DeleteCloudSaveAsync(SaveFile cloudSave)
    {
        if (SelectedCloudConfig == null)
            return (false, "未选择云端配置");

        IsLoading = true;
        StatusMessage = $"正在删除云端存档 \"{cloudSave.Name}\"...";

        try
        {
            var cloudService = new CloudStorageService(SelectedCloudConfig, _configService);
            await cloudService.DeleteSaveAsync(cloudSave);
            StatusMessage = $"云端存档 \"{cloudSave.Name}\" 已删除";
            return (true, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
            return (false, StatusMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}
