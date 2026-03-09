using GameSave.Helpers;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 本地文件系统存储服务
/// 负责 .tar 存档的备份、获取、恢复和删除
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly ConfigService _configService;

    public LocalStorageService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// 获取指定游戏的所有本地存档列表
    /// 扫描工作目录中对应游戏 ID 目录下的所有 .tar 文件
    /// </summary>
    public Task<List<SaveFile>> GetSavesAsync(string gameId)
    {
        var gameWorkDir = _configService.GetGameWorkDirectory(gameId);
        var saves = new List<SaveFile>();

        if (!Directory.Exists(gameWorkDir))
            return Task.FromResult(saves);

        foreach (var tarFile in Directory.GetFiles(gameWorkDir, "*.tar"))
        {
            var fileName = Path.GetFileNameWithoutExtension(tarFile);
            var parsed = ParseTarFileName(fileName);

            if (parsed.HasValue)
            {
                var fileInfo = new FileInfo(tarFile);
                saves.Add(new SaveFile
                {
                    Id = Guid.NewGuid().ToString(),
                    GameId = gameId,
                    Name = parsed.Value.tagName,
                    Path = tarFile,
                    BackupTime = parsed.Value.time,
                    SizeBytes = fileInfo.Length,
                    StorageType = StorageType.Local,
                    Tag = parsed.Value.tagName == "退出存档" ? SaveTag.ExitSave : SaveTag.ManualSave,
                    Description = parsed.Value.tagName == "退出存档" ? "游戏退出后自动备份" : null
                });
            }
        }

        // 按备份时间降序排列（最新的在前）
        saves.Sort((a, b) => b.BackupTime.CompareTo(a.BackupTime));
        return Task.FromResult(saves);
    }

    /// <summary>
    /// 创建存档备份
    /// 将游戏存档目录打包为 .tar 文件
    /// </summary>
    public async Task<SaveFile> BackupSaveAsync(Game game, string backupName, string? description = null)
    {
        if (!Directory.Exists(game.SaveFolderPath))
            throw new DirectoryNotFoundException($"游戏存档目录不存在: {game.SaveFolderPath}");

        // 检查存档目录是否有文件
        if (!DirectoryHasFiles(game.SaveFolderPath))
            throw new InvalidOperationException("存档目录下没有可备份的文件");

        var gameWorkDir = _configService.GetGameWorkDirectory(game.Id);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tarFileName = $"{timestamp}_{backupName}.tar";
        var tarFilePath = Path.Combine(gameWorkDir, tarFileName);

        // 打包存档目录为 .tar
        await TarHelper.CreateTarAsync(game.SaveFolderPath, tarFilePath);

        var fileInfo = new FileInfo(tarFilePath);
        var saveFile = new SaveFile
        {
            GameId = game.Id,
            Name = backupName,
            Path = tarFilePath,
            BackupTime = DateTime.Now,
            SizeBytes = fileInfo.Length,
            StorageType = StorageType.Local,
            Tag = backupName == "退出存档" ? SaveTag.ExitSave : SaveTag.ManualSave,
            Description = description
        };

        return saveFile;
    }

    /// <summary>
    /// 恢复存档
    /// 校验 .tar 文件，清空游戏存档目录后解压恢复
    /// </summary>
    public async Task RestoreSaveAsync(SaveFile saveFile)
    {
        // 校验 tar 文件有效性
        var isValid = await TarHelper.ValidateTarAsync(saveFile.Path);
        if (!isValid)
            throw new InvalidOperationException("存档文件已损坏，无法恢复");

        // 获取游戏信息以确定存档目录
        var game = _configService.GetGameById(saveFile.GameId);
        if (game == null)
            throw new InvalidOperationException($"找不到对应的游戏信息 (ID: {saveFile.GameId})");

        // 清空游戏存档目录
        if (Directory.Exists(game.SaveFolderPath))
        {
            ClearDirectory(game.SaveFolderPath);
        }

        // 解压 .tar 到游戏存档目录
        await TarHelper.ExtractTarAsync(saveFile.Path, game.SaveFolderPath);
    }

    /// <summary>
    /// 删除指定存档文件
    /// </summary>
    public Task DeleteSaveAsync(SaveFile saveFile)
    {
        if (saveFile.Tag == SaveTag.ExitSave)
            throw new InvalidOperationException("退出存档不允许删除");

        if (File.Exists(saveFile.Path))
        {
            File.Delete(saveFile.Path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 创建退出存档（自动覆盖旧的退出存档）
    /// </summary>
    public async Task<SaveFile> CreateExitSaveAsync(Game game)
    {
        var gameWorkDir = _configService.GetGameWorkDirectory(game.Id);

        // 删除旧的退出存档
        foreach (var oldExitSave in Directory.GetFiles(gameWorkDir, "*_退出存档.tar"))
        {
            File.Delete(oldExitSave);
        }

        // 创建新的退出存档
        return await BackupSaveAsync(game, "退出存档", "游戏退出后自动备份");
    }

    /// <summary>
    /// 获取最新的退出存档（用于启动游戏时恢复）
    /// </summary>
    public Task<SaveFile?> GetLatestExitSaveAsync(string gameId)
    {
        var gameWorkDir = _configService.GetGameWorkDirectory(gameId);

        if (!Directory.Exists(gameWorkDir))
            return Task.FromResult<SaveFile?>(null);

        var exitSaves = Directory.GetFiles(gameWorkDir, "*_退出存档.tar")
            .Select(f => new { Path = f, FileName = Path.GetFileNameWithoutExtension(f) })
            .Select(f => new { f.Path, Parsed = ParseTarFileName(f.FileName) })
            .Where(f => f.Parsed.HasValue)
            .OrderByDescending(f => f.Parsed!.Value.time)
            .FirstOrDefault();

        if (exitSaves == null)
            return Task.FromResult<SaveFile?>(null);

        var fileInfo = new FileInfo(exitSaves.Path);
        return Task.FromResult<SaveFile?>(new SaveFile
        {
            GameId = gameId,
            Name = "退出存档",
            Path = exitSaves.Path,
            BackupTime = exitSaves.Parsed!.Value.time,
            SizeBytes = fileInfo.Length,
            StorageType = StorageType.Local,
            Tag = SaveTag.ExitSave,
            Description = "游戏退出后自动备份"
        });
    }

    #region 辅助方法

    /// <summary>
    /// 解析 tar 文件名，提取时间戳和标签名
    /// 格式: {Unix时间戳}_{标签名}.tar
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

    /// <summary>
    /// 检查目录下是否有文件（递归）
    /// </summary>
    private static bool DirectoryHasFiles(string path)
    {
        return Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
    }

    /// <summary>
    /// 清空目录下所有内容（但保留目录本身）
    /// </summary>
    private static void ClearDirectory(string path)
    {
        var di = new DirectoryInfo(path);
        foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Delete();
        }
        foreach (var dir in di.EnumerateDirectories())
        {
            dir.Delete(true);
        }
    }

    #endregion
}
