using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 云端存储服务（待实现）
/// 支持 WebDAV / FTP / SFTP 等多种云存储后端
/// </summary>
public class CloudStorageService : IStorageService
{
    private readonly CloudConfig _config;

    public CloudStorageService(CloudConfig config)
    {
        _config = config;
    }

    // TODO: 根据 _config.ProviderType 选择不同的云存储后端实现
    public Task<List<SaveFile>> GetSavesAsync(string gameId) => throw new NotImplementedException();
    public Task<SaveFile> BackupSaveAsync(Game game, string backupName, string? description = null) => throw new NotImplementedException();
    public Task RestoreSaveAsync(SaveFile saveFile) => throw new NotImplementedException();
    public Task DeleteSaveAsync(SaveFile saveFile) => throw new NotImplementedException();
}
