using System.Text.Json.Serialization;

namespace GameSave.Models;

/// <summary>
/// 存档文件/备份点模型
/// </summary>
public class SaveFile
{
    /// <summary>存档唯一ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属游戏ID</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>存档名称（备份点名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>存档文件/目录路径</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>备份时间</summary>
    public DateTime BackupTime { get; set; } = DateTime.Now;

    /// <summary>显示用的备份时间</summary>
    [JsonIgnore]
    public string DisplayBackupTime => BackupTime.ToString("g");

    /// <summary>文件大小（字节）</summary>
    public long SizeBytes { get; set; }

    /// <summary>存储类型：Local / Cloud</summary>
    public StorageType StorageType { get; set; } = StorageType.Local;

    /// <summary>显示用的存储类型</summary>
    [JsonIgnore]
    public string DisplayStorageType => StorageType == StorageType.Local ? "本地" : "云端";

    /// <summary>存档标签类型</summary>
    public SaveTag Tag { get; set; } = SaveTag.ManualSave;

    /// <summary>显示用的存档标签</summary>
    [JsonIgnore]
    public string DisplayTag => Tag == SaveTag.ExitSave ? "退出存档" : "手动存档";

    /// <summary>是否为退出存档</summary>
    [JsonIgnore]
    public bool IsExitSave => Tag == SaveTag.ExitSave;

    /// <summary>是否可被用户删除（退出存档不可删除）</summary>
    [JsonIgnore]
    public bool CanDelete => Tag == SaveTag.ManualSave;

    /// <summary>是否可被用户恢复（退出存档不可手动恢复）</summary>
    [JsonIgnore]
    public bool CanRestore => Tag == SaveTag.ManualSave;

    /// <summary>友好显示文件大小</summary>
    [JsonIgnore]
    public string DisplaySize
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
            return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    /// <summary>描述/备注</summary>
    public string? Description { get; set; }
}

/// <summary>存储类型枚举</summary>
public enum StorageType
{
    /// <summary>本地存储</summary>
    Local,
    /// <summary>云端存储</summary>
    Cloud
}

/// <summary>存档标签枚举</summary>
public enum SaveTag
{
    /// <summary>退出存档 — 游戏退出后自动备份，仅保留最新一份</summary>
    ExitSave,
    /// <summary>手动存档 — 用户手动创建，可拥有多份</summary>
    ManualSave
}
