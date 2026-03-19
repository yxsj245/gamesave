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
    /// 将多个源路径（目录或文件）打包为一个 .tar 文件
    /// 每个源路径在 tar 中以序号子目录（0, 1, 2...）隔离存储
    /// 支持目录和单独文件混合打包
    /// </summary>
    /// <param name="sourcePaths">源路径列表（可以是目录或文件）</param>
    /// <param name="outputPath">输出 .tar 文件路径</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task CreateTarFromMultipleAsync(List<string> sourcePaths, string outputPath, IProgress<double>? progress = null)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
            throw new ArgumentException("至少需要一个源路径");

        // 如果只有一个路径，根据是文件还是目录分别处理
        if (sourcePaths.Count == 1)
        {
            var singlePath = sourcePaths[0];
            if (File.Exists(singlePath) && !Directory.Exists(singlePath))
            {
                // 单文件打包
                await CreateTarFromSingleFileAsync(singlePath, outputPath, progress);
            }
            else
            {
                await CreateTarAsync(singlePath, outputPath, progress);
            }
            return;
        }

        // 确保输出目录存在
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        int totalFiles = 0;
        int[] processedFiles = { 0 };
        if (progress != null)
        {
            foreach (var path in sourcePaths)
            {
                if (Directory.Exists(path))
                    totalFiles += Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
                else if (File.Exists(path))
                    totalFiles += 1;
            }
            progress.Report(0);
        }

        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var tarWriter = new TarWriter(fileStream);

        for (int i = 0; i < sourcePaths.Count; i++)
        {
            var sourcePath = sourcePaths[i];
            if (Directory.Exists(sourcePath))
            {
                // 目录：使用索引作为 tar 条目名称前缀来隔离不同源目录
                var prefix = i.ToString();
                await AddPrefixedDirectoryToTarAsync(tarWriter, sourcePath, prefix, "", progress, totalFiles, processedFiles);
            }
            else if (File.Exists(sourcePath))
            {
                // 单文件：以 索引/文件名 的形式存入 tar
                var entryName = $"{i}/{Path.GetFileName(sourcePath)}";
                await tarWriter.WriteEntryAsync(sourcePath, entryName);
                if (progress != null)
                {
                    processedFiles[0]++;
                    double pct = totalFiles > 0 ? (double)processedFiles[0] / totalFiles * 100 : 100;
                    progress.Report(Math.Min(100, pct));
                }
            }
        }
    }

    /// <summary>
    /// 将单个文件打包为 .tar 文件
    /// </summary>
    private static async Task CreateTarFromSingleFileAsync(string sourceFile, string outputPath, IProgress<double>? progress = null)
    {
        if (!File.Exists(sourceFile))
            throw new FileNotFoundException($"源文件不存在: {sourceFile}");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        progress?.Report(0);

        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var tarWriter = new TarWriter(fileStream);

        var entryName = Path.GetFileName(sourceFile);
        await tarWriter.WriteEntryAsync(sourceFile, entryName);

        progress?.Report(100);
    }

    /// <summary>
    /// 将 .tar 文件解包到多个目标路径（目录或文件所在目录）
    /// 根据 tar 中的顶层序号目录（0, 1, 2...）分别解压到对应的目标路径
    /// 支持目录和文件混合目标路径
    /// </summary>
    /// <param name="tarPath">.tar 文件路径</param>
    /// <param name="targetPaths">目标路径列表（与打包时的顺序对应，可以是目录或文件路径）</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task ExtractTarToMultipleAsync(string tarPath, List<string> targetPaths, IProgress<double>? progress = null)
    {
        if (targetPaths == null || targetPaths.Count == 0)
            throw new ArgumentException("至少需要一个目标路径");

        // 将目标路径解析为实际解压目录：文件路径取其父目录，目录路径直接使用
        var targetDirs = targetPaths.Select(ResolveTargetDirectory).ToList();

        // 如果只有一个目标路径，直接使用单目录方法
        if (targetDirs.Count == 1)
        {
            await ExtractTarAsync(tarPath, targetDirs[0], progress);
            return;
        }

        if (!File.Exists(tarPath))
            throw new FileNotFoundException($"Tar 文件不存在: {tarPath}");

        // 确保所有目标目录存在
        foreach (var dir in targetDirs)
        {
            Directory.CreateDirectory(dir);
        }

        int totalEntries = 0;
        int processedEntries = 0;
        if (progress != null)
        {
            totalEntries = await GetEntryCountAsync(tarPath);
            progress.Report(0);
        }

        await using var fileStream = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
        await using var tarReader = new TarReader(fileStream);

        // 先检查 tar 是否为多目录格式（顶层为数字目录名），否则退化到单目录解压
        bool isMultiFormat = await IsMultiDirectoryFormatAsync(tarPath);

        if (!isMultiFormat)
        {
            // 旧格式（单目录）：全部解压到第一个目标目录
            fileStream.Position = 0;
            await using var reader2 = new TarReader(fileStream);
            await ExtractReaderToDir(reader2, targetDirs[0], targetDirs[0], progress, totalEntries);
            return;
        }

        // 多目录格式：按序号前缀分发到不同的目标目录
        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            // 解析顶层目录名（序号）
            var entryName = entry.Name.Replace('\\', '/');
            var firstSlash = entryName.IndexOf('/');
            if (firstSlash < 0)
            {
                processedEntries++;
                continue;
            }

            var topDirName = entryName[..firstSlash];
            if (!int.TryParse(topDirName, out var dirIndex) || dirIndex < 0 || dirIndex >= targetDirs.Count)
            {
                processedEntries++;
                continue;
            }

            // 去掉顶层目录前缀，得到相对路径
            var relativePath = entryName[(firstSlash + 1)..];
            if (string.IsNullOrEmpty(relativePath))
            {
                processedEntries++;
                continue;
            }

            var destPath = Path.Combine(targetDirs[dirIndex], relativePath.Replace('/', Path.DirectorySeparatorChar));

            // 安全检查
            var fullDestPath = Path.GetFullPath(destPath);
            var fullTargetDir = Path.GetFullPath(targetDirs[dirIndex]);
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
    /// 检查 tar 文件是否为多目录格式（顶层条目为数字命名的目录）
    /// </summary>
    private static async Task<bool> IsMultiDirectoryFormatAsync(string tarPath)
    {
        try
        {
            await using var fs = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
            await using var reader = new TarReader(fs);

            // 检查前几个条目
            while (await reader.GetNextEntryAsync() is { } entry)
            {
                var name = entry.Name.Replace('\\', '/').TrimEnd('/');
                var firstSlash = name.IndexOf('/');
                var topDir = firstSlash > 0 ? name[..firstSlash] : name;

                // 如果顶层是数字目录名，认为是多目录格式
                if (int.TryParse(topDir, out _))
                    return true;

                // 如果不是数字目录名，认为是旧的单目录格式
                return false;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 辅助方法：将 TarReader 的内容解压到指定目录
    /// </summary>
    private static async Task ExtractReaderToDir(TarReader reader, string targetDir, string securityBaseDir, IProgress<double>? progress, int totalEntries)
    {
        int processed = 0;
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            var destPath = Path.Combine(targetDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
            var fullDestPath = Path.GetFullPath(destPath);
            var fullTargetDir = Path.GetFullPath(securityBaseDir);
            if (!fullDestPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"检测到不安全的 tar 条目路径: {entry.Name}");

            if (entry.EntryType == TarEntryType.Directory)
                Directory.CreateDirectory(destPath);
            else if (entry.EntryType == TarEntryType.RegularFile)
            {
                var fileDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(fileDir))
                    Directory.CreateDirectory(fileDir);
                await entry.ExtractToFileAsync(destPath, overwrite: true);
            }

            if (progress != null)
            {
                processed++;
                double pct = totalEntries > 0 ? (double)processed / totalEntries * 100 : 100;
                progress.Report(Math.Min(100, pct));
            }
        }
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
    /// 递归添加目录内容到 tar 归档（带前缀版本，用于多目录打包）
    /// entryPrefix 仅用于 tar 条目名称前缀，不参与文件系统路径导航
    /// </summary>
    private static async Task AddPrefixedDirectoryToTarAsync(TarWriter writer, string baseDir, string entryPrefix, string relativePath, IProgress<double>? progress, int totalFiles, int[] processedFiles)
    {
        // 文件系统导航：使用 baseDir + relativePath（不涉及 entryPrefix）
        var currentDir = string.IsNullOrEmpty(relativePath) ? baseDir : Path.Combine(baseDir, relativePath);

        // 添加文件
        foreach (var filePath in Directory.GetFiles(currentDir))
        {
            // tar 条目名称：entryPrefix/relativePath/fileName
            var innerPath = string.IsNullOrEmpty(relativePath)
                ? Path.GetFileName(filePath)
                : Path.Combine(relativePath, Path.GetFileName(filePath)).Replace('\\', '/');
            var entryName = $"{entryPrefix}/{innerPath}";

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

            await AddPrefixedDirectoryToTarAsync(writer, baseDir, entryPrefix, newRelativePath, progress, totalFiles, processedFiles);
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

    /// <summary>
    /// 将目标路径解析为实际的解压目录
    /// 如果路径指向一个已存在的文件（非目录），则返回文件所在的父目录
    /// 否则将路径视为目录路径直接返回
    /// </summary>
    private static string ResolveTargetDirectory(string targetPath)
    {
        // 如果路径已经是一个存在的目录，直接返回
        if (Directory.Exists(targetPath))
            return targetPath;

        // 如果路径是一个存在的文件，返回其父目录
        if (File.Exists(targetPath))
        {
            var parentDir = Path.GetDirectoryName(targetPath);
            return !string.IsNullOrEmpty(parentDir) ? parentDir : targetPath;
        }

        // 路径不存在时，检查是否有扩展名来判断是文件还是目录
        if (Path.HasExtension(targetPath))
        {
            var parentDir = Path.GetDirectoryName(targetPath);
            return !string.IsNullOrEmpty(parentDir) ? parentDir : targetPath;
        }

        // 默认当作目录路径
        return targetPath;
    }
}
