namespace GameSave.Helpers;

/// <summary>
/// 还原存档（清空目标后解压）前的路径安全预检。
/// 只做校验并返回规范化后的目标路径，不创建、不删除任何文件。
/// 调用方必须先对全部目标调用本类完成校验，再开始删除，
/// 避免"先删一个再校验另一个"导致部分清空。
/// 判定策略：
/// 1. 用户数据目录（磁盘根、用户目录、文档、桌面、AppData、ProgramData、OneDrive 等）
///    拒绝目录本身及其上级，但允许其中的专用游戏子目录；
/// 2. Windows 等操作系统目录拒绝目录本身、其上级及其全部子目录；程序目录只保护根及上级；
/// 3. 目标之间不能重复或互相包含；
/// 4. 目标不能包含软件工作目录、正在还原的 .tar 与 config.json；
/// 5. 目标本身及其上级、目录树内都不能存在重解析点（符号链接、目录联接）。
/// </summary>
public static class RestorePathSafety
{
    /// <summary>用户数据目录：本身与上级不能作为还原目标，其中的专用子目录允许。</summary>
    private static readonly Environment.SpecialFolder[] UserDataRoots =
    {
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.Desktop,
        Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.MyMusic,
        Environment.SpecialFolder.MyPictures,
        Environment.SpecialFolder.MyVideos,
        Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.CommonDocuments,
        Environment.SpecialFolder.CommonDesktopDirectory,
        Environment.SpecialFolder.CommonMusic,
        Environment.SpecialFolder.CommonPictures,
        Environment.SpecialFolder.CommonVideos,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.CommonProgramFiles,
        Environment.SpecialFolder.CommonProgramFilesX86
    };

    /// <summary>系统目录：本身、上级与全部子目录都不能作为还原目标。</summary>
    private static readonly Environment.SpecialFolder[] SystemRoots =
    {
        Environment.SpecialFolder.Windows,
        Environment.SpecialFolder.System,
        Environment.SpecialFolder.SystemX86,
        Environment.SpecialFolder.Fonts,
        Environment.SpecialFolder.StartMenu,
        Environment.SpecialFolder.Programs,
        Environment.SpecialFolder.Startup,
        Environment.SpecialFolder.CommonStartMenu,
        Environment.SpecialFolder.CommonPrograms,
        Environment.SpecialFolder.CommonStartup,
        Environment.SpecialFolder.AdminTools,
        Environment.SpecialFolder.Templates,
        Environment.SpecialFolder.CommonTemplates,
        Environment.SpecialFolder.Recent,
        Environment.SpecialFolder.SendTo,
        Environment.SpecialFolder.History,
        Environment.SpecialFolder.InternetCache,
        Environment.SpecialFolder.Cookies,
        Environment.SpecialFolder.CDBurning,
        Environment.SpecialFolder.PrinterShortcuts,
        Environment.SpecialFolder.NetworkShortcuts,
        Environment.SpecialFolder.Resources,
        Environment.SpecialFolder.LocalizedResources
    };

    /// <summary>
    /// 校验并规范化全部还原目标。
    /// </summary>
    /// <param name="saveFolderPaths">游戏的存档路径列表，可以是目录，也可以是单个存档文件。</param>
    /// <param name="workDirectory">软件工作目录（存档备份的存放位置），为空时跳过与它的比对。</param>
    /// <param name="archivePath">本次要还原的 .tar 存档文件路径。</param>
    /// <param name="configFilePath">软件的 config.json 路径，为空时跳过与它的比对。</param>
    /// <returns>规范化后的目标路径，顺序与输入一致；空白条目会被忽略。</returns>
    /// <exception cref="ArgumentException">路径不是完整的普通路径。</exception>
    /// <exception cref="InvalidOperationException">路径会危及无关资料、备份或配置文件时抛出；
    /// 抛出后调用方不得删除任何文件。</exception>
    public static IReadOnlyList<string> ResolveRestoreTargets(
        IEnumerable<string>? saveFolderPaths,
        string? workDirectory,
        string? archivePath,
        string? configFilePath)
    {
        var targets = new List<string>();
        if (saveFolderPaths != null)
        {
            foreach (var rawPath in saveFolderPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var target = NormalizeTargetPath(rawPath);
                EnsureTargetIsNotLink(target, rawPath);
                targets.Add(target);
            }
        }

        EnsureTargetsAreDisjoint(targets);

        foreach (var target in targets)
        {
            EnsureDisjointFromWorkDirectory(target, workDirectory);
            EnsureArchiveOutsideTarget(target, archivePath);
            EnsureConfigOutsideTarget(target, configFilePath);
        }

        // 目录树内的重解析点会让清空操作顺着联接删到目标之外，必须在删除前逐个拒绝。
        foreach (var target in targets)
        {
            EnsureNoLinkInsideTree(target);
        }

        return targets;
    }

    /// <summary>规范化单个目标，并拒绝磁盘根、文档、桌面、用户与系统目录。</summary>
    public static string NormalizeTargetPath(string rawPath)
    {
        string fullPath;
        try
        {
            fullPath = WorkDirectorySafety.NormalizePath(rawPath);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"存档目录必须是完整的普通文件夹路径，不支持相对路径或设备路径：{rawPath}", nameof(rawPath), ex);
        }

        try
        {
            // 复用工作目录切换已有的系统目录清单（等值判定）。
            WorkDirectorySafety.NormalizeDestination(fullPath);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"为避免清空无关资料，存档目录不能直接设为磁盘根目录、文档、桌面、用户或系统目录：{rawPath}", ex);
        }
        catch (ArgumentException)
        {
            // fullPath 已在上面确认是完整路径，这里的异常只可能来自该清单中无法解析的环境变量
            // （例如异常的 OneDrive 取值），交给下面的自有关键目录判定兜底，不因此中断还原。
        }

        EnsureIsNotProtectedRoot(fullPath, rawPath);
        return fullPath;
    }

    /// <summary>拒绝磁盘根目录、用户数据目录（含其上级）与系统目录（含其子目录）。</summary>
    private static void EnsureIsNotProtectedRoot(string fullPath, string rawPath)
    {
        EnsureNotUserDataRoot(fullPath, rawPath);
        EnsureNotInsideSystemDirectory(fullPath, rawPath);
    }

    /// <summary>
    /// 用户数据目录：目录本身与其上级不能作为还原目标（会清空无关资料），
    /// 但允许其中的专用游戏子目录，例如"文档\某游戏\存档"。
    /// </summary>
    private static void EnsureNotUserDataRoot(string fullPath, string rawPath)
    {
        var candidates = new List<string?> { Path.GetPathRoot(fullPath) };
        foreach (var folder in UserDataRoots) candidates.Add(TryGetFolderPath(folder));
        candidates.Add(Environment.GetEnvironmentVariable("OneDrive"));
        candidates.Add(Environment.GetEnvironmentVariable("OneDriveConsumer"));
        candidates.Add(Environment.GetEnvironmentVariable("OneDriveCommercial"));

        foreach (var protectedPath in NormalizeCandidates(candidates))
        {
            if (IsSameOrParent(fullPath, protectedPath))
            {
                throw new InvalidOperationException(
                    "为避免清空无关资料，存档目录不能是磁盘根目录、文档、桌面、用户或其他用户数据目录，" +
                    $"也不能是它们的上级目录；请在其中新建专用的游戏存档文件夹：{rawPath}");
            }
        }
    }

    /// <summary>
    /// 系统目录：目录本身、其上级以及全部子目录都不能作为还原目标，
    /// 例如不能允许 C:\Windows\System32 这类系统路径被清空。程序安装目录中的专用游戏存档由其它边界校验。
    /// </summary>
    private static void EnsureNotInsideSystemDirectory(string fullPath, string rawPath)
    {
        var candidates = new List<string?> { Environment.SystemDirectory };
        foreach (var folder in SystemRoots) candidates.Add(TryGetFolderPath(folder));

        foreach (var protectedPath in NormalizeCandidates(candidates))
        {
            if (IsSameOrParent(fullPath, protectedPath) || IsSameOrParent(protectedPath, fullPath))
            {
                throw new InvalidOperationException(
                    $"为避免清空系统文件，存档目录不能设在 Windows 等操作系统目录及其子目录中：{rawPath}");
            }
        }
    }

    /// <summary>读取特殊目录路径；本机不可用时返回 null（不使用 Create 选项，绝不创建目录）。</summary>
    private static string? TryGetFolderPath(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            // 该特殊目录在本机不可用，跳过。
            return null;
        }
    }

    /// <summary>把候选路径规范成可比较形式；无法解析的候选（例如异常的环境变量取值）直接跳过。</summary>
    private static List<string> NormalizeCandidates(IEnumerable<string?> candidates)
    {
        var normalized = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                normalized.Add(WorkDirectorySafety.NormalizePath(candidate));
            }
            catch (ArgumentException)
            {
                // 环境变量内容本身异常时跳过这一项，不影响其他判定。
            }
        }
        return normalized;
    }

    /// <summary>多个目标之间不能重复，也不能互相包含。</summary>
    private static void EnsureTargetsAreDisjoint(List<string> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            for (var j = i + 1; j < targets.Count; j++)
            {
                if (IsSameOrParent(targets[i], targets[j]) || IsSameOrParent(targets[j], targets[i]))
                {
                    throw new InvalidOperationException(
                        $"存档目录之间不能重复或互相包含，请把它们分别设置为独立的文件夹：{targets[i]} 与 {targets[j]}");
                }
            }
        }
    }

    /// <summary>目标不能与软件工作目录（备份存放位置）相同或互相包含。</summary>
    private static void EnsureDisjointFromWorkDirectory(string target, string? workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory)) return;

        var normalized = NormalizeComparisonPath(workDirectory, "软件工作目录");
        if (IsSameOrParent(target, normalized) || IsSameOrParent(normalized, target))
        {
            throw new InvalidOperationException(
                $"存档目录不能与软件工作目录（存档备份的存放位置）相同或互相包含，否则还原会破坏备份：{target}");
        }
    }

    /// <summary>目标不能包含正在还原的 .tar，否则清空目录会把源存档一起删掉。</summary>
    private static void EnsureArchiveOutsideTarget(string target, string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new InvalidOperationException("待还原的存档文件路径为空，为避免误删已取消还原。");

        var archive = NormalizeComparisonPath(archivePath, "待还原的存档文件");
        if (IsSameOrParent(target, archive))
        {
            throw new InvalidOperationException(
                $"存档目录包含了正在还原的存档文件，清空目录会把它一起删除，已取消还原：{archive}");
        }
    }

    /// <summary>目标不能包含软件的 config.json，否则清空目录会破坏配置。</summary>
    private static void EnsureConfigOutsideTarget(string target, string? configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath)) return;

        var config = NormalizeComparisonPath(configFilePath, "配置文件");
        if (IsSameOrParent(target, config))
        {
            throw new InvalidOperationException(
                $"存档目录包含了软件的配置文件，清空目录会破坏配置，已取消还原：{config}");
        }
    }

    /// <summary>目标本身及其上级不能是符号链接或目录联接。</summary>
    private static void EnsureTargetIsNotLink(string target, string rawPath)
    {
        try
        {
            WorkDirectorySafety.EnsureNoReparsePoints(target);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"存档目录或其上级存在符号链接或目录联接，为避免删到目标之外的文件，已取消还原：{rawPath}", ex);
        }
    }

    /// <summary>
    /// 目标目录树内不能存在任何重解析点（符号链接、目录联接）。
    /// 清空操作使用 AllDirectories 枚举，会跟随重解析点删到目标目录之外。
    /// </summary>
    private static void EnsureNoLinkInsideTree(string target)
    {
        if (!Directory.Exists(target)) return;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };

        var pending = new Stack<string>();
        pending.Push(target);
        try
        {
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop(), "*", options))
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                    {
                        // 枚举过程中条目消失：跳过，不影响其余检查。
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"存档目录内存在符号链接或目录联接，清空目录会顺着它删到目标之外，已取消还原：{entry}");
                    }

                    // 只对普通目录继续向下检查，绝不跟随重解析点。
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法完整确认时失败关闭：宁可不还原，也不冒越界删除的风险。
            throw new InvalidOperationException(
                $"无法确认存档目录内是否存在符号链接或目录联接，为避免误删已取消还原：{target}", ex);
        }
    }

    /// <summary>把外部路径规范成可比较的绝对路径；无法比较时失败关闭，不做猜测。</summary>
    private static string NormalizeComparisonPath(string path, string description)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)
                || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"无法用普通路径比较{description}的位置，为避免误删已取消还原：{path}");
            }
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"无法确认{description}的位置，为避免误删已取消还原：{path}", ex);
        }
    }

    /// <summary>判断 candidate 是否等于 parent 或位于 parent 之内。</summary>
    private static bool IsSameOrParent(string parent, string candidate)
    {
        if (candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
