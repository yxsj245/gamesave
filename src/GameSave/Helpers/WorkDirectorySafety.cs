namespace GameSave.Helpers;

/// <summary>工作目录切换的路径安全边界，不执行删除或自动清理。</summary>
public static class WorkDirectorySafety
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("请选择普通的完整文件夹路径，不支持相对路径或设备路径。", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string NormalizeDestination(string path)
    {
        var fullPath = NormalizePath(path);
        var protectedPaths = new List<string?> { Path.GetPathRoot(fullPath) };
        foreach (var folder in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            protectedPaths.Add(Environment.GetFolderPath(folder));
        protectedPaths.Add(Environment.GetEnvironmentVariable("OneDrive"));
        protectedPaths.Add(Environment.GetEnvironmentVariable("OneDriveConsumer"));
        protectedPaths.Add(Environment.GetEnvironmentVariable("OneDriveCommercial"));
        if (protectedPaths.Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => string.Equals(fullPath, NormalizePath(value!), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("不能直接使用磁盘根目录、文档、桌面或其他系统目录，请在其中新建专用的游戏备份文件夹。");
        return fullPath;
    }

    public static void EnsureDisjoint(string source, string target)
    {
        if (IsWithin(source, target) || IsWithin(target, source))
            throw new InvalidOperationException("新旧工作目录不能相同，也不能互相包含，请选择独立的文件夹。");
    }

    private static bool IsWithin(string parent, string candidate)
    {
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureNoReparsePoints(string path)
    {
        for (var current = NormalizePath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"为避免复制到其他位置，不支持符号链接或目录联接：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId is "." or ".."
            || gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || gameId.EndsWith('.') || gameId.EndsWith(' '))
            throw new InvalidOperationException("游戏标识包含非法路径，无法安全迁移，请检查导入的数据。");
    }
}