using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 存储服务公共接口
/// 所有存储后端（本地/云端）都实现此接口
/// </summary>
public interface IStorageService
{
    /// <summary>获取指定游戏的所有存档备份列表</summary>
    Task<List<SaveFile>> GetSavesAsync(string gameId);

    /// <summary>创建存档备份</summary>
    Task<SaveFile> BackupSaveAsync(Game game, string backupName, string? description = null);

    /// <summary>还原存档备份</summary>
    Task RestoreSaveAsync(SaveFile saveFile);

    /// <summary>删除指定存档备份</summary>
    Task DeleteSaveAsync(SaveFile saveFile);
}
