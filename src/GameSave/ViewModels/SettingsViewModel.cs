namespace GameSave.ViewModels;

/// <summary>
/// 设置页面 ViewModel
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private readonly Services.ConfigService _configService;

    public SettingsViewModel()
    {
        Title = "设置";
        _configService = App.ConfigService;
    }

    private string _workDirectory = string.Empty;
    public string WorkDirectory
    {
        get => _workDirectory;
        set => SetProperty(ref _workDirectory, value);
    }

    /// <summary>
    /// 初始化加载当前工作目录
    /// </summary>
    public void LoadSettings()
    {
        WorkDirectory = _configService.WorkDirectory;
    }

    /// <summary>
    /// 更改工作目录
    /// </summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        await _configService.ChangeWorkDirectoryAsync(newPath);
        WorkDirectory = newPath;
    }
}
