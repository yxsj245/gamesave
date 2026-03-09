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

    /// <summary>文件大小（字节）</summary>
    public long SizeBytes { get; set; }

    /// <summary>存储类型：Local / Cloud</summary>
    public StorageType StorageType { get; set; } = StorageType.Local;

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
