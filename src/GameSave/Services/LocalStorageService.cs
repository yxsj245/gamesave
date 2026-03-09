using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 本地文件系统存储服务（待实现）
/// </summary>
public class LocalStorageService : IStorageService
{
    // TODO: 实现本地存档管理逻辑
    public Task<List<SaveFile>> GetSavesAsync(string gameId) => throw new NotImplementedException();
    public Task<SaveFile> BackupSaveAsync(Game game, string backupName, string? description = null) => throw new NotImplementedException();
    public Task RestoreSaveAsync(SaveFile saveFile) => throw new NotImplementedException();
    public Task DeleteSaveAsync(SaveFile saveFile) => throw new NotImplementedException();
}
