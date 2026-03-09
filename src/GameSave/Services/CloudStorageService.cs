using System.Text.Json;
using System.Text.Json.Serialization;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 云端存储服务
/// 基于 CloudConfig 配置，当前支持阿里云 OSS 后端
/// 实现 IStorageService 接口，并提供上传/下载/测试连接等扩展方法
/// </summary>
public class CloudStorageService : IStorageService
{
    private readonly CloudConfig _config;
    private readonly ConfigService _configService;

    public CloudStorageService(CloudConfig config, ConfigService configService)
    {
        _config = config;
        _configService = configService;
    }

    /// <summary>
    /// 获取指定游戏的所有云端存档
    /// 扫描 OSS 中 {basePath}/{gameId}/ 前缀下的所有 .tar 文件
    /// </summary>
    public async Task<List<SaveFile>> GetSavesAsync(string gameId)
    {
        var saves = new List<SaveFile>();

        using var provider = CreateProvider();
        var prefix = $"{gameId}/";
        var objects = await provider.ListObjectsAsync(prefix);

        foreach (var obj in objects)
        {
            // 文件名格式: {gameId}/{timestamp}_{tagName}.tar
            var fileName = Path.GetFileNameWithoutExtension(obj.Key);
            if (!obj.Key.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                continue;

            var parsed = ParseTarFileName(fileName);
            if (parsed.HasValue)
            {
                saves.Add(new SaveFile
                {
                    Id = Guid.NewGuid().ToString(),
                    GameId = gameId,
                    Name = parsed.Value.tagName,
                    Path = obj.Key, // 云端存储使用相对 key 作为 Path
                    BackupTime = parsed.Value.time,
                    SizeBytes = obj.Size,
                    StorageType = StorageType.Cloud,
                    Tag = parsed.Value.tagName == "退出存档" ? SaveTag.ExitSave : SaveTag.ManualSave,
                    Description = $"云端存档 ({_config.DisplayName})"
                });
            }
        }

        // 按备份时间降序
        saves.Sort((a, b) => b.BackupTime.CompareTo(a.BackupTime));
        return saves;
    }

    /// <summary>
    /// 云端存档分组信息（包含游戏元数据）
    /// </summary>
    public class CloudSaveGroupInfo
    {
        /// <summary>游戏 ID</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>云端保存的游戏元数据（若有 game.json）</summary>
        public Game? CloudGameMetadata { get; set; }

        /// <summary>该游戏下的云端存档列表</summary>
        public List<SaveFile> Saves { get; set; } = new();
    }

    /// <summary>
    /// 获取所有游戏的云端存档（按游戏分组），同时读取云端游戏元数据
    /// </summary>
    public async Task<List<CloudSaveGroupInfo>> GetAllSavesGroupedAsync()
    {
        var groupDict = new Dictionary<string, CloudSaveGroupInfo>();

        using var provider = CreateProvider();
        var allObjects = await provider.ListObjectsAsync();

        foreach (var obj in allObjects)
        {
            // 解析 key 结构: {gameId}/xxx
            var slashIndex = obj.Key.IndexOf('/');
            if (slashIndex <= 0)
                continue;

            var gameId = obj.Key[..slashIndex];

            // 确保分组存在
            if (!groupDict.ContainsKey(gameId))
                groupDict[gameId] = new CloudSaveGroupInfo { GameId = gameId };

            // 跳过 game.json（后面单独处理）
            if (obj.Key.EndsWith("game.json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!obj.Key.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(obj.Key[(slashIndex + 1)..]);
            var parsed = ParseTarFileName(fileName);

            if (parsed.HasValue)
            {
                groupDict[gameId].Saves.Add(new SaveFile
                {
                    Id = Guid.NewGuid().ToString(),
                    GameId = gameId,
                    Name = parsed.Value.tagName,
                    Path = obj.Key,
                    BackupTime = parsed.Value.time,
                    SizeBytes = obj.Size,
                    StorageType = StorageType.Cloud,
                    Tag = parsed.Value.tagName == "退出存档" ? SaveTag.ExitSave : SaveTag.ManualSave,
                    Description = $"云端存档 ({_config.DisplayName})"
                });
            }
        }

        // 尝试读取每个游戏的元数据
        foreach (var group in groupDict.Values)
        {
            try
            {
                var json = await provider.DownloadContentAsync($"{group.GameId}/game.json");
                if (json != null)
                {
                    group.CloudGameMetadata = JsonSerializer.Deserialize<Game>(json, _jsonOptions);
                }
            }
            catch
            {
                // 元数据读取失败不影响存档列表
                System.Diagnostics.Debug.WriteLine($"[云端] 读取 {group.GameId}/game.json 失败");
            }
        }

        // 每组按时间降序
        foreach (var group in groupDict.Values)
        {
            group.Saves.Sort((a, b) => b.BackupTime.CompareTo(a.BackupTime));
        }

        return groupDict.Values.ToList();
    }

    /// <summary>
    /// 将本地 .tar 存档上传到云端
    /// </summary>
    /// <param name="game">游戏信息</param>
    /// <param name="backupName">备份名称（用于构造文件名）</param>
    /// <param name="description">描述</param>
    public Task<SaveFile> BackupSaveAsync(Game game, string backupName, string? description = null, IProgress<double>? progress = null)
    {
        // 此方法在云端场景下用于上传：先找本地最新对应名称的存档，再上传
        // 但更推荐使用 UploadSaveFileAsync 直接上传指定的本地存档文件
        throw new NotSupportedException("请使用 UploadSaveFileAsync 方法上传指定存档文件到云端");
    }

    /// <summary>
    /// 上传指定的本地存档文件到云端
    /// </summary>
    /// <param name="localSaveFile">本地存档文件信息</param>
    /// <param name="game">游戏信息</param>
    /// <param name="progress">上传进度回调</param>
    public async Task<SaveFile> UploadSaveFileAsync(SaveFile localSaveFile, Game game, IProgress<double>? progress = null)
    {
        if (!File.Exists(localSaveFile.Path))
            throw new FileNotFoundException($"本地存档文件不存在: {localSaveFile.Path}");

        var tarFileName = Path.GetFileName(localSaveFile.Path);
        var ossKey = $"{game.Id}/{tarFileName}";

        using var provider = CreateProvider();
        await provider.UploadFileAsync(localSaveFile.Path, ossKey, progress);

        var fileInfo = new FileInfo(localSaveFile.Path);
        return new SaveFile
        {
            GameId = game.Id,
            Name = localSaveFile.Name,
            Path = ossKey,
            BackupTime = localSaveFile.BackupTime,
            SizeBytes = fileInfo.Length,
            StorageType = StorageType.Cloud,
            Tag = localSaveFile.Tag,
            Description = $"已上传到 {_config.DisplayName}"
        };
    }

    /// <summary>
    /// 从云端下载存档到本地工作目录
    /// </summary>
    /// <param name="cloudSaveFile">云端存档信息（Path 为 OSS Key）</param>
    /// <param name="progress">下载进度回调</param>
    public async Task<string> DownloadSaveToLocalAsync(SaveFile cloudSaveFile, IProgress<double>? progress = null)
    {
        var localDir = _configService.GetGameWorkDirectory(cloudSaveFile.GameId);
        var tarFileName = Path.GetFileName(cloudSaveFile.Path);
        var localFilePath = Path.Combine(localDir, tarFileName);

        using var provider = CreateProvider();
        await provider.DownloadFileAsync(cloudSaveFile.Path, localFilePath, progress);

        return localFilePath;
    }

    /// <summary>
    /// 恢复云端存档（先下载到本地，再由 LocalStorageService 解压恢复）
    /// </summary>
    public async Task RestoreSaveAsync(SaveFile saveFile, IProgress<double>? progress = null)
    {
        // 下载到本地工作目录
        await DownloadSaveToLocalAsync(saveFile, progress);
        // 恢复操作由调用方配合 LocalStorageService 完成
    }

    /// <summary>
    /// 上传游戏元数据到云端（game.json）
    /// </summary>
    /// <param name="game">游戏信息</param>
    public async Task UploadGameMetadataAsync(Game game)
    {
        var json = JsonSerializer.Serialize(game, _jsonOptions);
        var ossKey = $"{game.Id}/game.json";

        using var provider = CreateProvider();
        await provider.UploadContentAsync(json, ossKey);
        System.Diagnostics.Debug.WriteLine($"[云端] 已上传游戏元数据: {game.Name} ({game.Id})");
    }

    /// <summary>
    /// 获取云端游戏元数据
    /// </summary>
    /// <param name="gameId">游戏 ID</param>
    /// <returns>游戏信息，若不存在则返回 null</returns>
    public async Task<Game?> GetGameMetadataAsync(string gameId)
    {
        using var provider = CreateProvider();
        var json = await provider.DownloadContentAsync($"{gameId}/game.json");
        if (json == null) return null;

        return JsonSerializer.Deserialize<Game>(json, _jsonOptions);
    }

    /// <summary>
    /// 删除云端存档
    /// </summary>
    public async Task DeleteSaveAsync(SaveFile saveFile)
    {
        using var provider = CreateProvider();
        await provider.DeleteObjectAsync(saveFile.Path);
    }

    /// <summary>
    /// 测试云端连接
    /// </summary>
    public async Task<(bool success, string message)> TestConnectionAsync()
    {
        try
        {
            using var provider = CreateProvider();
            return await provider.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            return (false, $"连接测试失败: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 创建底层存储提供者实例
    /// </summary>
    private OssStorageProvider CreateProvider()
    {
        if (_config.ProviderType != CloudProviderType.AliyunOss)
            throw new NotSupportedException($"暂不支持 {_config.ProviderType} 云存储类型，当前仅支持阿里云 OSS");

        return new OssStorageProvider(_config);
    }

    /// <summary>
    /// 解析 tar 文件名，提取时间戳和标签名
    /// 格式: {Unix时间戳}_{标签名}
    /// </summary>
    private static (DateTime time, string tagName)? ParseTarFileName(string fileNameWithoutExtension)
    {
        var separatorIndex = fileNameWithoutExtension.IndexOf('_');
        if (separatorIndex <= 0)
            return null;

        var timestampStr = fileNameWithoutExtension[..separatorIndex];
        var tagName = fileNameWithoutExtension[(separatorIndex + 1)..];

        if (long.TryParse(timestampStr, out var unixTimestamp))
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).LocalDateTime;
            return (time, tagName);
        }

        return null;
    }
}
