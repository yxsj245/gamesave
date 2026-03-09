namespace GameSave.Models;

/// <summary>
/// 游戏信息模型
/// </summary>
public class Game
{
    /// <summary>游戏唯一ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>游戏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>游戏存档目录路径</summary>
    public string SaveFolderPath { get; set; } = string.Empty;

    /// <summary>游戏图标路径（可选）</summary>
    public string? IconPath { get; set; }

    /// <summary>添加时间</summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string DisplayAddedAt => AddedAt.ToString("yyyy/MM/dd");

    /// <summary>备注</summary>
    public string? Notes { get; set; }
}
