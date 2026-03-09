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

    /// <summary>游戏名称（若本地已添加则显示名称，否则显示 ID）</summary>
    public string GameName { get; set; } = string.Empty;

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
            var grouped = await cloudService.GetAllSavesGroupedAsync();

            foreach (var (gameId, saves) in grouped)
            {
                // 尝试从本地配置找到游戏名称
                var game = _configService.GetGameById(gameId);
                var group = new CloudSaveGroup
                {
                    GameId = gameId,
                    GameName = game?.Name ?? $"未知游戏 ({gameId[..8]}...)"
                };

                foreach (var save in saves)
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

    #region 下载云端存档

    /// <summary>
    /// 下载云端存档到本地
    /// </summary>
    public async Task<(bool success, string message)> DownloadSaveAsync(SaveFile cloudSave)
    {
        if (SelectedCloudConfig == null)
            return (false, "未选择云端配置");

        IsLoading = true;
        StatusMessage = $"正在下载存档 \"{cloudSave.Name}\"...";

        try
        {
            var cloudService = new CloudStorageService(SelectedCloudConfig, _configService);
            var localPath = await cloudService.DownloadSaveToLocalAsync(cloudSave);
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
