using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameSave.Helpers;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameSave.Models;

/// <summary>
/// 游戏信息模型
/// </summary>
public class Game : INotifyPropertyChanged
{
    /// <summary>游戏唯一ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>游戏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>游戏存档目录路径（可能包含环境变量，如 %APPDATA%\Game）</summary>
    public string SaveFolderPath { get; set; } = string.Empty;

    /// <summary>展开环境变量后的实际存档目录路径（用于文件系统操作）</summary>
    [JsonIgnore]
    public string ResolvedSaveFolderPath => PathEnvironmentHelper.ExpandEnvVariables(SaveFolderPath);

    /// <summary>游戏图标路径（可选）</summary>
    public string? IconPath { get; set; }

    /// <summary>游戏启动进程路径（可选，用于自动检测游戏运行状态）</summary>
    public string? ProcessPath { get; set; }

    /// <summary>启动附加参数（可选）</summary>
    public string? ProcessArgs { get; set; }

    /// <summary>关联的云端服务商配置 ID（可选）</summary>
    public string? CloudConfigId { get; set; }

    /// <summary>添加时间</summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;

    /// <summary>显示用的添加日期</summary>
    [JsonIgnore]
    public string DisplayAddedAt => AddedAt.ToString("yyyy/MM/dd");

    /// <summary>备注</summary>
    public string? Notes { get; set; }

    /// <summary>是否启用定时备份</summary>
    public bool ScheduledBackupEnabled { get; set; } = false;

    /// <summary>定时备份间隔（分钟）</summary>
    public int ScheduledBackupIntervalMinutes { get; set; } = 30;

    /// <summary>定时备份最大保留数量</summary>
    public int ScheduledBackupMaxCount { get; set; } = 5;

    private bool _isScheduledBackupRunning;
    /// <summary>定时备份是否正在运行（运行时状态，不序列化）</summary>
    [JsonIgnore]
    public bool IsScheduledBackupRunning
    {
        get => _isScheduledBackupRunning;
        set
        {
            if (_isScheduledBackupRunning != value)
            {
                _isScheduledBackupRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanShowScheduledBackupStart));
                OnPropertyChanged(nameof(CanShowScheduledBackupStop));
            }
        }
    }

    /// <summary>是否显示手动启动定时备份按钮（无启动进程 + 已启用定时备份 + 未运行中）</summary>
    [JsonIgnore]
    public bool CanShowScheduledBackupStart => !HasProcessPath && ScheduledBackupEnabled && !IsScheduledBackupRunning;

    /// <summary>是否显示手动停止定时备份按钮（无启动进程 + 已启用定时备份 + 运行中）</summary>
    [JsonIgnore]
    public bool CanShowScheduledBackupStop => !HasProcessPath && ScheduledBackupEnabled && IsScheduledBackupRunning;

    /// <summary>是否设置了启动进程路径</summary>
    [JsonIgnore]
    public bool HasProcessPath => !string.IsNullOrWhiteSpace(ProcessPath);

    private BitmapImage? _gameIconSource;
    /// <summary>从游戏 EXE 提取的图标（缓存后返回 BitmapImage）</summary>
    [JsonIgnore]
    public BitmapImage? GameIconSource
    {
        get
        {
            if (_gameIconSource == null && HasProcessPath)
            {
                _gameIconSource = IconExtractorHelper.GetIconFromExe(ProcessPath);
            }
            return _gameIconSource;
        }
    }

    /// <summary>是否有可用的游戏图标（用于 XAML 显示切换）</summary>
    [JsonIgnore]
    public bool HasGameIcon => GameIconSource != null;

    /// <summary>
    /// 刷新图标缓存（ProcessPath 变更后调用）
    /// </summary>
    public void RefreshIcon()
    {
        IconExtractorHelper.ClearCache(ProcessPath);
        _gameIconSource = null;
        OnPropertyChanged(nameof(GameIconSource));
        OnPropertyChanged(nameof(HasGameIcon));
    }

    private bool _isRunning;
    /// <summary>指示游戏当前是否正在运行</summary>
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
                // 进程状态改变时，重置停止中状态
                if (!value)
                {
                    IsStopping = false;
                    RunningPid = 0;
                }
                // 同时通知启动/停止按钮可见性变化
                OnPropertyChanged(nameof(CanShowStartButton));
                OnPropertyChanged(nameof(CanShowStopButton));
            }
        }
    }

    private int _runningPid;
    /// <summary>当前运行的进程 PID</summary>
    [JsonIgnore]
    public int RunningPid
    {
        get => _runningPid;
        set
        {
            if (_runningPid != value)
            {
                _runningPid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayRunningPid));
            }
        }
    }

    /// <summary>显示用的进程 PID 文本</summary>
    [JsonIgnore]
    public string DisplayRunningPid => RunningPid > 0 ? $"PID: {RunningPid}" : string.Empty;

    private string _launchStatusMessage = string.Empty;
    /// <summary>启动进度状态消息（在列表项启动按钮左侧显示）</summary>
    [JsonIgnore]
    public string LaunchStatusMessage
    {
        get => _launchStatusMessage;
        set
        {
            if (_launchStatusMessage != value)
            {
                _launchStatusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLaunchStatus));
            }
        }
    }

    /// <summary>是否有启动状态消息需要显示</summary>
    [JsonIgnore]
    public bool HasLaunchStatus => !string.IsNullOrEmpty(LaunchStatusMessage);

    /// <summary>是否显示启动按钮（有进程路径且未运行且未在停止中）</summary>
    [JsonIgnore]
    public bool CanShowStartButton => HasProcessPath && !IsRunning && !IsStopping;

    private bool _isStopping;
    /// <summary>指示游戏是否正在停止中（显示转圈加载反馈）</summary>
    [JsonIgnore]
    public bool IsStopping
    {
        get => _isStopping;
        set
        {
            if (_isStopping != value)
            {
                _isStopping = value;
                OnPropertyChanged();
                // 停止中时隐藏停止按钮，显示加载指示
                OnPropertyChanged(nameof(CanShowStopButton));
                OnPropertyChanged(nameof(CanShowStartButton));
            }
        }
    }

    /// <summary>是否显示停止按钮（有进程路径且正在运行且未在停止中）</summary>
    [JsonIgnore]
    public bool CanShowStopButton => HasProcessPath && IsRunning && !IsStopping;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
