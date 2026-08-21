using System.Collections.ObjectModel;
using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.ViewModels;

/// <summary>
/// 设置页面 ViewModel
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private readonly ConfigService _configService;
    private readonly IGlobalHotkeyService? _globalHotkeyService;

    /// <summary>
    /// 应用版本号（从程序集信息中读取，构建时由 MSBuild 注入）
    /// </summary>
    public string AppVersion { get; } = AppVersionHelper.GetVersion();

    public SettingsViewModel()
        : this(App.ConfigService, App.GlobalHotkeyService)
    {
    }

    public SettingsViewModel(ConfigService configService, IGlobalHotkeyService? globalHotkeyService = null)
    {
        Title = "设置";
        _configService = configService;
        _globalHotkeyService = globalHotkeyService;
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

    #region 快捷存档快捷键

    private bool _isQuickSaveHotkeyEnabled;
    public bool IsQuickSaveHotkeyEnabled
    {
        get => _isQuickSaveHotkeyEnabled;
        private set => SetProperty(ref _isQuickSaveHotkeyEnabled, value);
    }

    private HotkeyGesture _quickSaveHotkey = HotkeyGesture.Default;
    public HotkeyGesture QuickSaveHotkey
    {
        get => _quickSaveHotkey;
        private set
        {
            if (SetProperty(ref _quickSaveHotkey, value))
                OnPropertyChanged(nameof(QuickSaveHotkeyDisplayText));
        }
    }

    public string QuickSaveHotkeyDisplayText => QuickSaveHotkey.ToDisplayString();

    private string _quickSaveHotkeyStatusMessage = string.Empty;
    public string QuickSaveHotkeyStatusMessage
    {
        get => _quickSaveHotkeyStatusMessage;
        private set => SetProperty(ref _quickSaveHotkeyStatusMessage, value);
    }

    public bool IsQuickSaveHotkeyStatusError { get; private set; }

    /// <summary>
    /// 先更新系统注册，成功后再持久化；失败时保留原配置。
    /// </summary>
    public async Task<(bool success, string message)> ApplyQuickSaveHotkeyAsync(bool enabled, HotkeyGesture gesture)
    {
        if (!gesture.IsValid(out var validationMessage))
        {
            SetQuickSaveHotkeyStatus(validationMessage, isError: true);
            return (false, validationMessage);
        }

        if (_globalHotkeyService == null)
        {
            const string unavailableMessage = "全局快捷键服务尚未初始化";
            SetQuickSaveHotkeyStatus(unavailableMessage, isError: true);
            return (false, unavailableMessage);
        }

        if (!_globalHotkeyService.TryUpdateRegistration(enabled, gesture, out var registrationMessage))
        {
            SetQuickSaveHotkeyStatus(registrationMessage, isError: true);
            return (false, registrationMessage);
        }

        try
        {
            await _configService.SetQuickSaveHotkeyAsync(enabled, gesture);
            IsQuickSaveHotkeyEnabled = enabled;
            QuickSaveHotkey = gesture;
            SetQuickSaveHotkeyStatus(registrationMessage, isError: false);
            return (true, registrationMessage);
        }
        catch (Exception ex)
        {
            // 配置写入失败时恢复到原有注册状态。
            _globalHotkeyService.TryUpdateRegistration(
                IsQuickSaveHotkeyEnabled,
                QuickSaveHotkey,
                out _);
            var failureMessage = $"快捷键配置保存失败：{ex.Message}";
            SetQuickSaveHotkeyStatus(failureMessage, isError: true);
            return (false, failureMessage);
        }
    }

    private void SetQuickSaveHotkeyStatus(string message, bool isError)
    {
        IsQuickSaveHotkeyStatusError = isError;
        QuickSaveHotkeyStatusMessage = message;
        OnPropertyChanged(nameof(IsQuickSaveHotkeyStatusError));
    }

    #endregion

    #region 开机自启动

    /// <summary>
    /// 是否启用开机自启动
    /// </summary>
    private bool _isAutoStartEnabled;
    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set
        {
            if (SetProperty(ref _isAutoStartEnabled, value))
            {
                _ = ToggleAutoStartAsync(value);
            }
        }
    }

    /// <summary>
    /// 自启动操作结果提示
    /// </summary>
    private string _autoStartStatusMessage = string.Empty;
    public string AutoStartStatusMessage
    {
        get => _autoStartStatusMessage;
        set => SetProperty(ref _autoStartStatusMessage, value);
    }

    /// <summary>
    /// 切换开机自启动
    /// </summary>
    private async Task ToggleAutoStartAsync(bool enabled)
    {
        var success = AutoStartService.SetAutoStart(enabled);
        if (success)
        {
            await _configService.SetAutoStartAsync(enabled);
            AutoStartStatusMessage = string.Empty;
        }
        else
        {
            // 操作失败，回滚 UI 状态
            _isAutoStartEnabled = !enabled;
            OnPropertyChanged(nameof(IsAutoStartEnabled));
            AutoStartStatusMessage = "操作失败，请检查系统权限";
        }
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
    /// 工作目录是否被启动参数锁定（锁定时不可更改）
    /// </summary>
    private bool _isWorkDirLocked;
    public bool IsWorkDirLocked
    {
        get => _isWorkDirLocked;
        set => SetProperty(ref _isWorkDirLocked, value);
    }

    /// <summary>
    /// 工作目录锁定原因提示文本
    /// </summary>
    private string _workDirLockReason = string.Empty;
    public string WorkDirLockReason
    {
        get => _workDirLockReason;
        set => SetProperty(ref _workDirLockReason, value);
    }

    /// <summary>
    /// 初始化加载当前工作目录、云配置和主题设置
    /// </summary>
    public void LoadSettings()
    {
        WorkDirectory = _configService.WorkDirectory;
        LoadCloudConfigs();

        // 加载工作目录锁定状态
        IsWorkDirLocked = ConfigService.IsWorkDirLockedByArgs;
        WorkDirLockReason = IsWorkDirLocked ? "工作目录由启动参数 --workdir 指定，无法在此更改" : string.Empty;

        // 加载当前主题设置（直接设置字段避免触发 setter 中的保存操作）
        _selectedThemeIndex = ThemeModeToIndex(_configService.ThemeMode);
        OnPropertyChanged(nameof(SelectedThemeIndex));

        // 加载开机自启动状态（以注册表实际状态为准）
        _isAutoStartEnabled = AutoStartService.IsAutoStartEnabled();
        OnPropertyChanged(nameof(IsAutoStartEnabled));

        IsQuickSaveHotkeyEnabled = _configService.QuickSaveHotkeyEnabled;
        QuickSaveHotkey = _configService.QuickSaveHotkey;
        SetQuickSaveHotkeyStatus(
            _globalHotkeyService?.StatusMessage ?? "全局快捷键服务尚未初始化",
            _globalHotkeyService == null || (_configService.QuickSaveHotkeyEnabled && !_globalHotkeyService.IsRegistered));
    }

    /// <summary>
    /// 更改工作目录（不迁移旧内容）
    /// </summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        await _configService.ChangeWorkDirectoryAsync(newPath);
        WorkDirectory = newPath;
    }

    /// <summary>
    /// 更改工作目录并迁移旧内容到新目录
    /// </summary>
    /// <param name="newPath">新的工作目录路径</param>
    /// <param name="progress">进度回调</param>
    public async Task ChangeWorkDirectoryWithMigrationAsync(string newPath, IProgress<(double percent, string status)>? progress = null)
    {
        await _configService.ChangeWorkDirectoryWithMigrationAsync(newPath, progress);
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
    private CloudProviderType _newProviderType = CloudProviderType.AliyunOss;
    public CloudProviderType NewProviderType
    {
        get => _newProviderType;
        set => SetProperty(ref _newProviderType, value);
    }

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

    private string _newServerUrl = string.Empty;
    public string NewServerUrl
    {
        get => _newServerUrl;
        set => SetProperty(ref _newServerUrl, value);
    }

    private string _newUsername = string.Empty;
    public string NewUsername
    {
        get => _newUsername;
        set => SetProperty(ref _newUsername, value);
    }

    private string _newPassword = string.Empty;
    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    private CloudConfig? _editingCloudConfig;

    private bool _isEditingCloudConfig;
    public bool IsEditingCloudConfig
    {
        get => _isEditingCloudConfig;
        set => SetProperty(ref _isEditingCloudConfig, value);
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
        _editingCloudConfig = null;
        IsEditingCloudConfig = false;
        NewProviderType = CloudProviderType.AliyunOss;
        NewDisplayName = string.Empty;
        NewEndpoint = string.Empty;
        NewBucketName = string.Empty;
        NewAccessKeyId = string.Empty;
        NewAccessKeySecret = string.Empty;
        NewServerUrl = string.Empty;
        NewUsername = string.Empty;
        NewPassword = string.Empty;
        NewRemoteBasePath = "GameSave";
        TestResult = string.Empty;
    }

    /// <summary>
    /// 进入云端配置编辑模式，并把已有配置回填到表单字段。
    /// </summary>
    public void BeginEditCloudConfig(CloudConfig config)
    {
        _editingCloudConfig = config;
        IsEditingCloudConfig = true;
        NewProviderType = config.ProviderType;
        NewDisplayName = config.DisplayName;
        NewEndpoint = config.Endpoint ?? string.Empty;
        NewBucketName = config.BucketName ?? string.Empty;
        NewAccessKeyId = config.AccessKeyId ?? string.Empty;
        NewAccessKeySecret = config.AccessKeySecret ?? string.Empty;
        NewServerUrl = config.ServerUrl ?? string.Empty;
        NewUsername = config.Username ?? string.Empty;
        NewPassword = config.Password ?? string.Empty;
        NewRemoteBasePath = string.IsNullOrWhiteSpace(config.RemoteBasePath)
            ? GetDefaultRemoteBasePath(config.ProviderType)
            : config.RemoteBasePath;
        TestResult = string.Empty;
    }

    /// <summary>
    /// 验证云端配置表单（添加时验证）
    /// </summary>
    public (bool valid, string message) ValidateCloudConfigForm()
    {
        if (string.IsNullOrWhiteSpace(NewDisplayName))
            return (false, "请输入配置名称");

        if (NewProviderType == CloudProviderType.AliyunOss)
        {
            if (string.IsNullOrWhiteSpace(NewEndpoint))
                return (false, "请输入 OSS Endpoint");
            if (string.IsNullOrWhiteSpace(NewBucketName))
                return (false, "请输入 Bucket 名称");
            if (string.IsNullOrWhiteSpace(NewAccessKeyId))
                return (false, "请输入 AccessKey ID");
            if (string.IsNullOrWhiteSpace(NewAccessKeySecret))
                return (false, "请输入 AccessKey Secret");
        }
        else if (NewProviderType == CloudProviderType.WebDav)
        {
            if (string.IsNullOrWhiteSpace(NewServerUrl))
                return (false, "请输入 WebDAV 服务器地址");
            if (string.IsNullOrWhiteSpace(NewUsername))
                return (false, "请输入 WebDAV 用户名");
            if (string.IsNullOrWhiteSpace(NewPassword))
                return (false, "请输入 WebDAV 密码");
        }
        else
        {
            return (false, $"暂不支持 {NewProviderType} 云存储类型");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// 保存云端配置。新增模式下添加配置，编辑模式下更新原配置。
    /// </summary>
    public async Task<(bool success, string message)> SaveCloudConfigAsync()
    {
        var (valid, validMsg) = ValidateCloudConfigForm();
        if (!valid)
            return (false, validMsg);

        try
        {
            var editingConfig = _editingCloudConfig;
            var config = CreateCloudConfigFromForm(editingConfig);

            if (editingConfig != null)
            {
                await _configService.UpdateCloudConfigAsync(config);

                var index = CloudConfigs.IndexOf(editingConfig);
                if (index >= 0)
                    CloudConfigs[index] = config;
                else
                    LoadCloudConfigs();

                ResetCloudConfigForm();
                return (true, $"云端配置 \"{config.DisplayName}\" 保存成功");
            }

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

    private CloudConfig CreateCloudConfigFromForm(CloudConfig? existingConfig = null)
    {
        var config = new CloudConfig
        {
            Id = existingConfig?.Id ?? Guid.NewGuid().ToString(),
            DisplayName = NewDisplayName.Trim(),
            ProviderType = NewProviderType,
            RemoteBasePath = string.IsNullOrWhiteSpace(NewRemoteBasePath)
                ? GetDefaultRemoteBasePath(NewProviderType)
                : NewRemoteBasePath.Trim(),
            IsEnabled = existingConfig?.IsEnabled ?? true
        };

        if (NewProviderType == CloudProviderType.AliyunOss)
        {
            config.Endpoint = NewEndpoint.Trim();
            config.BucketName = NewBucketName.Trim();
            config.AccessKeyId = NewAccessKeyId.Trim();
            config.AccessKeySecret = NewAccessKeySecret.Trim();
        }
        else if (NewProviderType == CloudProviderType.WebDav)
        {
            config.ServerUrl = NewServerUrl.Trim();
            config.Username = NewUsername.Trim();
            config.Password = NewPassword;
        }

        return config;
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
                ProviderType = NewProviderType,
                RemoteBasePath = string.IsNullOrWhiteSpace(NewRemoteBasePath)
                    ? GetDefaultRemoteBasePath(NewProviderType)
                    : NewRemoteBasePath.Trim()
            };

            if (NewProviderType == CloudProviderType.AliyunOss)
            {
                config.Endpoint = NewEndpoint.Trim();
                config.BucketName = NewBucketName.Trim();
                config.AccessKeyId = NewAccessKeyId.Trim();
                config.AccessKeySecret = NewAccessKeySecret.Trim();
            }
            else if (NewProviderType == CloudProviderType.WebDav)
            {
                config.ServerUrl = NewServerUrl.Trim();
                config.Username = NewUsername.Trim();
                config.Password = NewPassword;
            }

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

    public static string GetDefaultRemoteBasePath(CloudProviderType providerType) => providerType switch
    {
        CloudProviderType.WebDav => "GameSave",
        _ => "GameSave"
    };

    #endregion

    #region 导入导出

    /// <summary>
    /// 导出选中的游戏及其存档到 zip 文件
    /// </summary>
    /// <param name="games">要导出的游戏列表</param>
    /// <param name="outputPath">输出 zip 文件路径</param>
    public async Task<(bool success, string message)> ExportGamesAsync(List<Game> games, string outputPath, IProgress<double>? progress = null)
    {
        try
        {
            var service = new ExportImportService(_configService);
            // 在后台线程执行 I/O 密集操作，确保 UI 线程能及时响应进度回调
            await Task.Run(async () => await service.ExportGamesAsync(games, outputPath, progress));
            return (true, $"成功导出 {games.Count} 个游戏到:\n{outputPath}");
        }
        catch (Exception ex)
        {
            return (false, $"导出失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取导入预览信息
    /// </summary>
    public async Task<(bool success, List<ImportGamePreview>? previews, string message)> GetImportPreviewAsync(string zipPath)
    {
        try
        {
            var service = new ExportImportService(_configService);
            var previews = await service.GetImportPreviewAsync(zipPath);

            if (previews.Count == 0)
                return (false, null, "导入文件中没有找到有效的游戏数据");

            return (true, previews, $"发现 {previews.Count} 个游戏");
        }
        catch (Exception ex)
        {
            return (false, null, $"读取导入文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 执行导入
    /// </summary>
    public async Task<(bool success, string message)> ImportGamesAsync(string zipPath, IProgress<double>? progress = null)
    {
        try
        {
            var service = new ExportImportService(_configService);
            // 在后台线程执行 I/O 密集操作，确保 UI 线程能及时响应进度回调
            var result = await Task.Run(async () => await service.ImportGamesAsync(zipPath, progress));
            return (true, $"导入完成: {result.imported} 个成功, {result.skipped} 个跳过\n\n{result.details}");
        }
        catch (Exception ex)
        {
            return (false, $"导入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取所有游戏列表（供导出选择用）
    /// </summary>
    public List<Game> GetAllGames()
    {
        return _configService.GetAllGames();
    }

    #endregion
}
