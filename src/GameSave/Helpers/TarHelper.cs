using System.Formats.Tar;

namespace GameSave.Helpers;

/// <summary>
/// Tar 归档文件助手类
/// 封装 .tar 文件的打包、解包和校验功能
/// </summary>
public static class TarHelper
{
    /// <summary>
    /// 将指定目录打包为 .tar 文件
    /// </summary>
    /// <param name="sourceDir">源目录路径</param>
    /// <param name="outputPath">输出 .tar 文件路径</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task CreateTarAsync(string sourceDir, string outputPath, IProgress<double>? progress = null)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"源目录不存在: {sourceDir}");

        // 确保输出目录存在
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        int totalFiles = 0;
        int[] processedFiles = { 0 };
        if (progress != null)
        {
            totalFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).Length;
            progress.Report(0);
        }

        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var tarWriter = new TarWriter(fileStream);

        await AddDirectoryToTarAsync(tarWriter, sourceDir, "", progress, totalFiles, processedFiles);
    }

    /// <summary>
    /// 递归添加目录内容到 tar 归档
    /// </summary>
    private static async Task AddDirectoryToTarAsync(TarWriter writer, string baseDir, string relativePath, IProgress<double>? progress, int totalFiles, int[] processedFiles)
    {
        var currentDir = string.IsNullOrEmpty(relativePath) ? baseDir : Path.Combine(baseDir, relativePath);

        // 添加文件
        foreach (var filePath in Directory.GetFiles(currentDir))
        {
            var entryName = string.IsNullOrEmpty(relativePath)
                ? Path.GetFileName(filePath)
                : Path.Combine(relativePath, Path.GetFileName(filePath)).Replace('\\', '/');

            await writer.WriteEntryAsync(filePath, entryName);
            if (progress != null)
            {
                processedFiles[0]++;
                double pct = totalFiles > 0 ? (double)processedFiles[0] / totalFiles * 100 : 100;
                progress.Report(Math.Min(100, pct));
            }
        }

        // 递归添加子目录
        foreach (var dirPath in Directory.GetDirectories(currentDir))
        {
            var dirName = Path.GetFileName(dirPath);
            var newRelativePath = string.IsNullOrEmpty(relativePath)
                ? dirName
                : Path.Combine(relativePath, dirName);

            await AddDirectoryToTarAsync(writer, baseDir, newRelativePath, progress, totalFiles, processedFiles);
        }
    }

    /// <summary>
    /// 将 .tar 文件解包到指定目录
    /// </summary>
    /// <param name="tarPath">.tar 文件路径</param>
    /// <param name="targetDir">目标解包目录</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task ExtractTarAsync(string tarPath, string targetDir, IProgress<double>? progress = null)
    {
        if (!File.Exists(tarPath))
            throw new FileNotFoundException($"Tar 文件不存在: {tarPath}");

        // 确保目标目录存在
        Directory.CreateDirectory(targetDir);

        int totalEntries = 0;
        int processedEntries = 0;
        if (progress != null)
        {
            totalEntries = await GetEntryCountAsync(tarPath);
            progress.Report(0);
        }

        await using var fileStream = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
        await using var tarReader = new TarReader(fileStream);

        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            var destPath = Path.Combine(targetDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));

            // 安全检查：防止路径遍历攻击
            var fullDestPath = Path.GetFullPath(destPath);
            var fullTargetDir = Path.GetFullPath(targetDir);
            if (!fullDestPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"检测到不安全的 tar 条目路径: {entry.Name}");
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destPath);
            }
            else if (entry.EntryType == TarEntryType.RegularFile)
            {
                // 确保文件所在目录存在
                var fileDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(fileDir))
                    Directory.CreateDirectory(fileDir);

                await entry.ExtractToFileAsync(destPath, overwrite: true);
            }

            if (progress != null)
            {
                processedEntries++;
                double pct = totalEntries > 0 ? (double)processedEntries / totalEntries * 100 : 100;
                progress.Report(Math.Min(100, pct));
            }
        }
    }

    /// <summary>
    /// 校验 .tar 文件是否有效（可正常读取）
    /// </summary>
    /// <param name="tarPath">.tar 文件路径</param>
    /// <returns>校验通过返回 true</returns>
    public static async Task<bool> ValidateTarAsync(string tarPath)
    {
        try
        {
            if (!File.Exists(tarPath))
                return false;

            // 尝试读取所有条目来验证文件完整性
            await using var fileStream = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
            await using var tarReader = new TarReader(fileStream);

            while (await tarReader.GetNextEntryAsync() is not null)
            {
                // 只要能正常遍历完所有条目，说明 tar 文件结构有效
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取 tar 文件中的条目数量（用于快速检查）
    /// </summary>
    public static async Task<int> GetEntryCountAsync(string tarPath)
    {
        if (!File.Exists(tarPath))
            return 0;

        int count = 0;
        await using var fileStream = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
        await using var tarReader = new TarReader(fileStream);

        while (await tarReader.GetNextEntryAsync() is not null)
        {
            count++;
        }

        return count;
    }
}
