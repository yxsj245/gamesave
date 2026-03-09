using System.Text.Json.Serialization;

using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    /// <summary>游戏存档目录路径</summary>
    public string SaveFolderPath { get; set; } = string.Empty;

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

    /// <summary>是否设置了启动进程路径</summary>
    [JsonIgnore]
    public bool HasProcessPath => !string.IsNullOrWhiteSpace(ProcessPath);

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
                // 同时通知启动/停止按钮可见性变化
                OnPropertyChanged(nameof(CanShowStartButton));
                OnPropertyChanged(nameof(CanShowStopButton));
            }
        }
    }

    /// <summary>是否显示启动按钮（有进程路径且未运行）</summary>
    [JsonIgnore]
    public bool CanShowStartButton => HasProcessPath && !IsRunning;

    /// <summary>是否显示停止按钮（有进程路径且正在运行）</summary>
    [JsonIgnore]
    public bool CanShowStopButton => HasProcessPath && IsRunning;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
