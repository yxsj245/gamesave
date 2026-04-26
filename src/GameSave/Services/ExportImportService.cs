using System.IO.Compression;
using System.Text.Json;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 导出清单模型
/// </summary>
public class ExportManifest
{
    /// <summary>导出版本号</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>导出时间</summary>
    public DateTime ExportTime { get; set; } = DateTime.Now;

    /// <summary>导出的游戏数量</summary>
    public int GameCount { get; set; }

    /// <summary>导出的游戏概要信息列表</summary>
    public List<ExportGameSummary> Games { get; set; } = new();
}

/// <summary>
/// 导出游戏概要信息（用于 manifest.json）
/// </summary>
public class ExportGameSummary
{
    /// <summary>游戏 ID</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>游戏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>存档文件数量</summary>
    public int SaveCount { get; set; }
}

/// <summary>
/// 导入预览中的游戏信息
/// </summary>
public class ImportGamePreview
{
    /// <summary>游戏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>游戏 ID</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>存档路径（含环境变量）</summary>
    public string SaveFolderPath { get; set; } = string.Empty;

    /// <summary>存档文件数量</summary>
    public int SaveCount { get; set; }

    /// <summary>是否已存在于本地（同名游戏）</summary>
    public bool AlreadyExists { get; set; }
}

/// <summary>
/// 导入导出服务
/// 封装游戏配置和存档数据的导出（zip）和导入功能
/// </summary>
public class ExportImportService
{
    private readonly ConfigService _configService;

    public ExportImportService(ConfigService configService)
    {
        _configService = configService;
    }

    #region 导出

    /// <summary>
    /// 导出指定游戏列表及其存档到 zip 文件
    /// </summary>
    /// <param name="games">要导出的游戏列表</param>
    /// <param name="outputPath">输出 zip 文件路径</param>
    /// <param name="progress">进度报告</param>
    public async Task ExportGamesAsync(List<Game> games, string outputPath, IProgress<double>? progress = null)
    {
        if (games.Count == 0)
            throw new InvalidOperationException("没有选择要导出的游戏");

        // 确保输出目录存在
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // 如果文件已存在则删除
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        progress?.Report(0);

        var manifest = new ExportManifest
        {
            GameCount = games.Count
        };

        int totalSteps = games.Count + 1; // 每个游戏 + 写 manifest
        int completedSteps = 0;

        using var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        foreach (var game in games)
        {
            var gameWorkDir = _configService.GetGameWorkDirectory(game.Id);

            // 写入 game.json
            var gameJsonEntry = archive.CreateEntry($"{game.Id}/game.json");
            await using (var entryStream = gameJsonEntry.Open())
            {
                await JsonSerializer.SerializeAsync(entryStream, game, AppJsonContext.Default.Game);
            }

            // 收集存档文件
            int saveCount = 0;
            if (Directory.Exists(gameWorkDir))
            {
                foreach (var tarFile in Directory.GetFiles(gameWorkDir, "*.tar"))
                {
                    var tarFileName = Path.GetFileName(tarFile);
                    var tarEntry = archive.CreateEntry($"{game.Id}/saves/{tarFileName}", CompressionLevel.NoCompression);

                    await using var tarFileStream = new FileStream(tarFile, FileMode.Open, FileAccess.Read);
                    await using var tarEntryStream = tarEntry.Open();
                    await tarFileStream.CopyToAsync(tarEntryStream);

                    saveCount++;
                }

                // 收集其他游戏资源文件，例如自定义图标
                foreach (var assetFile in Directory.GetFiles(gameWorkDir, "*", SearchOption.AllDirectories)
                             .Where(file => !file.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)))
                {
                    var relativePath = Path.GetRelativePath(gameWorkDir, assetFile).Replace('\\', '/');
                    var assetEntry = archive.CreateEntry($"{game.Id}/{relativePath}", CompressionLevel.Optimal);

                    await using var assetStream = new FileStream(assetFile, FileMode.Open, FileAccess.Read);
                    await using var assetEntryStream = assetEntry.Open();
                    await assetStream.CopyToAsync(assetEntryStream);
                }
            }

            manifest.Games.Add(new ExportGameSummary
            {
                Id = game.Id,
                Name = game.Name,
                SaveCount = saveCount
            });

            completedSteps++;
            progress?.Report((double)completedSteps / totalSteps * 100);
        }

        // 写入 manifest.json
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, AppJsonContext.Default.ExportManifest);
        }

        completedSteps++;
        progress?.Report(100);
    }

    #endregion

    #region 导入

    /// <summary>
    /// 预览 zip 文件中的游戏列表
    /// </summary>
    /// <param name="zipPath">zip 文件路径</param>
    /// <returns>导入预览信息列表</returns>
    public async Task<List<ImportGamePreview>> GetImportPreviewAsync(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"导入文件不存在: {zipPath}");

        var previews = new List<ImportGamePreview>();
        var existingGames = _configService.GetAllGames();

        using var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // 查找所有 game.json 条目
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith("/game.json", StringComparison.OrdinalIgnoreCase))
                continue;

            await using var entryStream = entry.Open();
            var game = await JsonSerializer.DeserializeAsync(entryStream, AppJsonContext.Default.Game);

            if (game == null) continue;

            // 统计该游戏的存档数量
            var gameId = Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/').TrimEnd('/') ?? "";
            int saveCount = archive.Entries.Count(e => IsImportedSaveEntry(gameId, e.FullName));

            // 检查是否已存在（名称匹配或 ID 匹配）
            bool alreadyExists = existingGames.Any(g =>
                g.Id == game.Id ||
                g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

            previews.Add(new ImportGamePreview
            {
                Name = game.Name,
                Id = game.Id,
                SaveFolderPath = game.SaveFolderPath,
                SaveCount = saveCount,
                AlreadyExists = alreadyExists
            });
        }

        return previews;
    }

    /// <summary>
    /// 从 zip 文件导入游戏及存档
    /// </summary>
    /// <param name="zipPath">zip 文件路径</param>
    /// <param name="progress">进度报告</param>
    /// <returns>导入结果摘要（成功数, 跳过数, 详细信息）</returns>
    public async Task<(int imported, int skipped, string details)> ImportGamesAsync(string zipPath, IProgress<double>? progress = null)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"导入文件不存在: {zipPath}");

        progress?.Report(0);

        var existingGames = _configService.GetAllGames();
        int imported = 0;
        int skipped = 0;
        var detailLines = new List<string>();

        using var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // 收集所有游戏条目
        var gameEntries = archive.Entries
            .Where(e => e.FullName.EndsWith("/game.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int totalGames = gameEntries.Count;
        int processedGames = 0;

        foreach (var gameEntry in gameEntries)
        {
            await using var entryStream = gameEntry.Open();
            var game = await JsonSerializer.DeserializeAsync(entryStream, AppJsonContext.Default.Game);

            if (game == null)
            {
                skipped++;
                detailLines.Add("⚠️ 跳过无效的游戏数据");
                processedGames++;
                progress?.Report((double)processedGames / totalGames * 100);
                continue;
            }

            // 检查是否已存在
            bool alreadyExists = existingGames.Any(g =>
                g.Id == game.Id ||
                g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                skipped++;
                detailLines.Add($"⏭️ 跳过已存在的游戏: {game.Name}");
                processedGames++;
                progress?.Report((double)processedGames / totalGames * 100);
                continue;
            }

            // 创建游戏工作目录
            var gameWorkDir = _configService.GetGameWorkDirectory(game.Id);

            // 提取该游戏的存档文件
            var gameId = Path.GetDirectoryName(gameEntry.FullName)?.Replace('\\', '/').TrimEnd('/') ?? "";
            var gameAssetEntries = archive.Entries
                .Where(e =>
                    e.FullName.StartsWith($"{gameId}/", StringComparison.OrdinalIgnoreCase) &&
                    !e.FullName.EndsWith("/game.json", StringComparison.OrdinalIgnoreCase) &&
                    !e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int savesExtracted = 0;
            foreach (var gameAssetEntry in gameAssetEntries)
            {
                var relativePath = GetImportRelativePath(gameId, gameAssetEntry.FullName);
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                var targetPath = Path.Combine(gameWorkDir, relativePath);

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                await using var saveStream = gameAssetEntry.Open();
                await using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                await saveStream.CopyToAsync(targetStream);

                if (targetPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    savesExtracted++;
            }

            // 重置添加时间为当前时间
            game.AddedAt = DateTime.Now;

            // 添加游戏到配置
            await _configService.AddGameAsync(game);

            imported++;
            detailLines.Add($"✅ 导入成功: {game.Name}（{savesExtracted} 个存档）");

            processedGames++;
            progress?.Report((double)processedGames / totalGames * 100);
        }

        progress?.Report(100);

        var details = string.Join("\n", detailLines);
        return (imported, skipped, details);
    }

    #endregion

    #region 导入路径兼容

    /// <summary>
    /// 判断导入包中的条目是否为存档文件。
    /// 兼容旧结构（根目录 tar）与当前导出结构（saves/tar）。
    /// </summary>
    private static bool IsImportedSaveEntry(string gameId, string entryFullName)
    {
        var relativePath = GetImportRelativePath(gameId, entryFullName);
        return !string.IsNullOrWhiteSpace(relativePath) &&
               relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将导入包中的相对路径转换为本地工作目录中的目标相对路径。
    /// 导出包中的 saves/*.tar 会自动平铺到工作目录根部，便于现有存档扫描逻辑识别。
    /// </summary>
    private static string? GetImportRelativePath(string gameId, string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(gameId) ||
            !entryFullName.StartsWith($"{gameId}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = entryFullName[(gameId.Length + 1)..].Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (relativePath.StartsWith("saves/", StringComparison.OrdinalIgnoreCase) &&
            relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(relativePath);
        }

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    #endregion
}
