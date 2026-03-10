using System.Collections.ObjectModel;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.ViewModels;

/// <summary>
/// 设置页面 ViewModel
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private readonly ConfigService _configService;

    public SettingsViewModel()
    {
        Title = "设置";
        _configService = App.ConfigService;
    }

    #region 外观设置（主题）

    /// <summary>
    /// 当前选中的主题索引：0=跟随系统，1=浅色，2=深色
    /// </summary>
    private int _selectedThemeIndex;
    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (SetProperty(ref _selectedThemeIndex, value))
            {
                _ = ChangeThemeAsync(value);
            }
        }
    }

    /// <summary>
    /// 主题索引到字符串的映射
    /// </summary>
    private static readonly string[] ThemeModes = { "System", "Light", "Dark" };

    /// <summary>
    /// 切换主题并保存
    /// </summary>
    private async Task ChangeThemeAsync(int themeIndex)
    {
        if (themeIndex < 0 || themeIndex >= ThemeModes.Length) return;
        var themeMode = ThemeModes[themeIndex];
        await App.SetThemeAsync(themeMode);
    }

    /// <summary>
    /// 将主题字符串转换为索引
    /// </summary>
    private static int ThemeModeToIndex(string themeMode)
    {
        return themeMode switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0  // "System" 或其他
        };
    }

    #endregion

    #region 工作目录

    private string _workDirectory = string.Empty;
    public string WorkDirectory
    {
        get => _workDirectory;
        set => SetProperty(ref _workDirectory, value);
    }

    /// <summary>
    /// 初始化加载当前工作目录、云配置和主题设置
    /// </summary>
    public void LoadSettings()
    {
        WorkDirectory = _configService.WorkDirectory;
        LoadCloudConfigs();

        // 加载当前主题设置（直接设置字段避免触发 setter 中的保存操作）
        _selectedThemeIndex = ThemeModeToIndex(_configService.ThemeMode);
        OnPropertyChanged(nameof(SelectedThemeIndex));
    }

    /// <summary>
    /// 更改工作目录
    /// </summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        await _configService.ChangeWorkDirectoryAsync(newPath);
        WorkDirectory = newPath;
    }

    #endregion

    #region 云端配置管理

    private ObservableCollection<CloudConfig> _cloudConfigs = new();
    /// <summary>已配置的云端服务商列表</summary>
    public ObservableCollection<CloudConfig> CloudConfigs
    {
        get => _cloudConfigs;
        set => SetProperty(ref _cloudConfigs, value);
    }

    // 添加云配置的表单字段
    private string _newDisplayName = string.Empty;
    public string NewDisplayName
    {
        get => _newDisplayName;
        set => SetProperty(ref _newDisplayName, value);
    }

    private string _newEndpoint = string.Empty;
    public string NewEndpoint
    {
        get => _newEndpoint;
        set => SetProperty(ref _newEndpoint, value);
    }

    private string _newBucketName = string.Empty;
    public string NewBucketName
    {
        get => _newBucketName;
        set => SetProperty(ref _newBucketName, value);
    }

    private string _newAccessKeyId = string.Empty;
    public string NewAccessKeyId
    {
        get => _newAccessKeyId;
        set => SetProperty(ref _newAccessKeyId, value);
    }

    private string _newAccessKeySecret = string.Empty;
    public string NewAccessKeySecret
    {
        get => _newAccessKeySecret;
        set => SetProperty(ref _newAccessKeySecret, value);
    }

    private string _newRemoteBasePath = "GameSave";
    public string NewRemoteBasePath
    {
        get => _newRemoteBasePath;
        set => SetProperty(ref _newRemoteBasePath, value);
    }

    private string _testResult = string.Empty;
    public string TestResult
    {
        get => _testResult;
        set => SetProperty(ref _testResult, value);
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

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
    }

    /// <summary>
    /// 重置添加云配置表单
    /// </summary>
    public void ResetCloudConfigForm()
    {
        NewDisplayName = string.Empty;
        NewEndpoint = string.Empty;
        NewBucketName = string.Empty;
        NewAccessKeyId = string.Empty;
        NewAccessKeySecret = string.Empty;
        NewRemoteBasePath = "GameSave";
        TestResult = string.Empty;
    }

    /// <summary>
    /// 验证云端配置表单（添加时验证）
    /// </summary>
    public (bool valid, string message) ValidateCloudConfigForm()
    {
        if (string.IsNullOrWhiteSpace(NewDisplayName))
            return (false, "请输入配置名称");
        if (string.IsNullOrWhiteSpace(NewEndpoint))
            return (false, "请输入 OSS Endpoint");
        if (string.IsNullOrWhiteSpace(NewBucketName))
            return (false, "请输入 Bucket 名称");
        if (string.IsNullOrWhiteSpace(NewAccessKeyId))
            return (false, "请输入 AccessKey ID");
        if (string.IsNullOrWhiteSpace(NewAccessKeySecret))
            return (false, "请输入 AccessKey Secret");

        return (true, string.Empty);
    }

    /// <summary>
    /// 添加云端配置
    /// </summary>
    public async Task<(bool success, string message)> AddCloudConfigAsync()
    {
        var (valid, validMsg) = ValidateCloudConfigForm();
        if (!valid)
            return (false, validMsg);

        try
        {
            var config = new CloudConfig
            {
                DisplayName = NewDisplayName.Trim(),
                ProviderType = CloudProviderType.AliyunOss,
                Endpoint = NewEndpoint.Trim(),
                BucketName = NewBucketName.Trim(),
                AccessKeyId = NewAccessKeyId.Trim(),
                AccessKeySecret = NewAccessKeySecret.Trim(),
                RemoteBasePath = string.IsNullOrWhiteSpace(NewRemoteBasePath)
                    ? "GameSave"
                    : NewRemoteBasePath.Trim()
            };

            await _configService.AddCloudConfigAsync(config);
            CloudConfigs.Add(config);
            ResetCloudConfigForm();

            return (true, $"云端配置 \"{config.DisplayName}\" 添加成功");
        }
        catch (Exception ex)
        {
            return (false, $"添加失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 删除云端配置
    /// </summary>
    public async Task<(bool success, string message)> RemoveCloudConfigAsync(CloudConfig config)
    {
        try
        {
            await _configService.RemoveCloudConfigAsync(config.Id);
            CloudConfigs.Remove(config);
            return (true, $"已删除配置 \"{config.DisplayName}\"");
        }
        catch (Exception ex)
        {
            return (false, $"删除失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 测试云端连接
    /// </summary>
    public async Task TestCloudConnectionAsync()
    {
        var (valid, validMsg) = ValidateCloudConfigForm();
        if (!valid)
        {
            TestResult = validMsg;
            return;
        }

        IsTesting = true;
        TestResult = "正在测试连接...";

        try
        {
            var config = new CloudConfig
            {
                ProviderType = CloudProviderType.AliyunOss,
                Endpoint = NewEndpoint.Trim(),
                BucketName = NewBucketName.Trim(),
                AccessKeyId = NewAccessKeyId.Trim(),
                AccessKeySecret = NewAccessKeySecret.Trim(),
                RemoteBasePath = string.IsNullOrWhiteSpace(NewRemoteBasePath)
                    ? "GameSave"
                    : NewRemoteBasePath.Trim()
            };

            var cloudService = new CloudStorageService(config, _configService);
            var (success, message) = await cloudService.TestConnectionAsync();
            TestResult = success ? $"✅ {message}" : $"❌ {message}";
        }
        catch (Exception ex)
        {
            TestResult = $"❌ 测试失败: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    #endregion
}
