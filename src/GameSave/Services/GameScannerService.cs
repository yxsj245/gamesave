using Microsoft.Win32;

namespace GameSave.Services;

/// <summary>
/// 检测到的本地游戏信息
/// </summary>
public class DetectedGame
{
    /// <summary>游戏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>游戏安装目录</summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>游戏主程序 exe 路径（可能为空）</summary>
    public string? ExePath { get; set; }

    /// <summary>来源平台（Steam / Epic / GOG / Ubisoft / EA / Battle.net）</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>是否被用户勾选导入</summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>用户补全的存档目录</summary>
    public string SaveFolderPath { get; set; } = string.Empty;

    /// <summary>用户补全的启动参数</summary>
    public string ProcessArgs { get; set; } = string.Empty;

    /// <summary>用户选择的云端配置 ID</summary>
    public string? CloudConfigId { get; set; }

    /// <summary>是否启用定时备份</summary>
    public bool ScheduledBackupEnabled { get; set; } = false;

    /// <summary>定时备份间隔（分钟）</summary>
    public int ScheduledBackupIntervalMinutes { get; set; } = 30;

    /// <summary>定时备份最大保留数量</summary>
    public int ScheduledBackupMaxCount { get; set; } = 5;
}

/// <summary>
/// 游戏平台扫描服务
/// 自动检测 Steam、Epic、GOG、Ubisoft、EA、Battle.net 等平台已安装的游戏
/// </summary>
public class GameScannerService
{
    /// <summary>
    /// 已知的非游戏 exe 文件名模式（用于排除）
    /// </summary>
    private static readonly string[] NonGameExePatterns =
    [
        "UnityCrashHandler",
        "UnityCrashHandler64",
        "UnityCrashHandler32",
        "unins000",
        "unins001",
        "uninstall",
        "CrashReportClient",
        "CrashReporter",
        "vcredist",
        "dxsetup",
        "dxwebsetup",
        "dotNetFx",
        "DXSETUP",
        "installer",
        "setup",
        "7z",
        "7za",
        "updater",
        "Updater",
        "launcher",  // 注意：这个可能误排较多，但大多数 launcher 不是游戏本体
    ];

    /// <summary>
    /// 扫描所有支持平台的已安装游戏
    /// </summary>
    /// <returns>检测到的游戏列表</returns>
    public async Task<List<DetectedGame>> ScanAllPlatformsAsync()
    {
        var allGames = new List<DetectedGame>();

        // 并行扫描各平台
        var tasks = new List<Task<List<DetectedGame>>>
        {
            Task.Run(() => ScanSteam()),
            Task.Run(() => ScanEpic()),
            Task.Run(() => ScanGOG()),
            Task.Run(() => ScanUbisoft()),
            Task.Run(() => ScanEA()),
            Task.Run(() => ScanBattleNet()),
        };

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            allGames.AddRange(result);
        }

        // 按安装路径去重（优先保留有 exe 路径的）
        var deduped = allGames
            .GroupBy(g => g.InstallPath.ToLowerInvariant().TrimEnd('\\', '/'))
            .Select(group => group.OrderByDescending(g => string.IsNullOrEmpty(g.ExePath) ? 0 : 1).First())
            .OrderBy(g => g.Name)
            .ToList();

        return deduped;
    }

    #region Steam 扫描

    /// <summary>
    /// 扫描 Steam 已安装的游戏
    /// </summary>
    private List<DetectedGame> ScanSteam()
    {
        var games = new List<DetectedGame>();

        try
        {
            // 1. 从注册表获取 Steam 安装路径
            var steamPath = GetRegistryValue(
                RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                "InstallPath"
            );

            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                return games;

            // 2. 解析 libraryfolders.vdf 获取所有 Steam 库路径
            var libraryPaths = ParseSteamLibraryFolders(steamPath);

            // 3. 遍历每个库中的 appmanifest 文件
            foreach (var libraryPath in libraryPaths)
            {
                var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                if (!Directory.Exists(steamAppsPath))
                    continue;

                var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        var game = ParseSteamAppManifest(manifestFile, steamAppsPath);
                        if (game != null)
                        {
                            games.Add(game);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GameScanner] 解析 Steam manifest 失败: {manifestFile}, {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] Steam 扫描异常: {ex.Message}");
        }

        return games;
    }

    /// <summary>
    /// 解析 Steam libraryfolders.vdf，获取所有 Steam 库路径
    /// </summary>
    private List<string> ParseSteamLibraryFolders(string steamPath)
    {
        var paths = new List<string> { steamPath };

        var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return paths;

        try
        {
            var content = File.ReadAllText(vdfPath);
            // 简单正则解析 "path" 字段
            var matches = System.Text.RegularExpressions.Regex.Matches(
                content,
                "\"path\"\\s+\"([^\"]+)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var libPath = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(libPath) && !paths.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(libPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] 解析 libraryfolders.vdf 失败: {ex.Message}");
        }

        return paths;
    }

    /// <summary>
    /// 解析单个 Steam appmanifest 文件
    /// </summary>
    private DetectedGame? ParseSteamAppManifest(string manifestPath, string steamAppsPath)
    {
        var content = File.ReadAllText(manifestPath);

        var nameMatch = System.Text.RegularExpressions.Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"");
        var installDirMatch = System.Text.RegularExpressions.Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"");

        if (!nameMatch.Success || !installDirMatch.Success)
            return null;

        var name = nameMatch.Groups[1].Value;
        var installDir = installDirMatch.Groups[1].Value;
        var fullInstallPath = Path.Combine(steamAppsPath, "common", installDir);

        if (!Directory.Exists(fullInstallPath))
            return null;

        // 排除 Steam 平台工具（Steamworks Common Redistributables 等）
        if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
            return null;

        // 查找主 exe
        var exePath = FindMainExe(fullInstallPath);

        return new DetectedGame
        {
            Name = name,
            InstallPath = fullInstallPath,
            ExePath = exePath,
            Source = "Steam"
        };
    }

    #endregion

    #region Epic 扫描

    /// <summary>
    /// 扫描 Epic Games 已安装的游戏
    /// </summary>
    private List<DetectedGame> ScanEpic()
    {
        var games = new List<DetectedGame>();

        try
        {
            var launcherInstalledPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat"
            );

            if (!File.Exists(launcherInstalledPath))
                return games;

            var json = File.ReadAllText(launcherInstalledPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("InstallationList", out var installList))
                return games;

            foreach (var item in installList.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("InstallLocation", out var locationProp))
                        continue;

                    var installLocation = locationProp.GetString();
                    if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                        continue;

                    // 获取应用名
                    var appName = item.TryGetProperty("AppName", out var nameProp)
                        ? nameProp.GetString() ?? Path.GetFileName(installLocation)
                        : Path.GetFileName(installLocation);

                    // 优先使用文件夹名作为显示名称（AppName 通常是内部 ID）
                    var displayName = Path.GetFileName(installLocation) ?? appName;

                    var exePath = FindMainExe(installLocation);

                    games.Add(new DetectedGame
                    {
                        Name = displayName,
                        InstallPath = installLocation,
                        ExePath = exePath,
                        Source = "Epic"
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GameScanner] 解析 Epic 游戏条目失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] Epic 扫描异常: {ex.Message}");
        }

        return games;
    }

    #endregion

    #region GOG 扫描

    /// <summary>
    /// 扫描 GOG Galaxy 已安装的游戏
    /// 注意：GOG 注册表中通常直接包含 exe 路径
    /// </summary>
    private List<DetectedGame> ScanGOG()
    {
        var games = new List<DetectedGame>();

        try
        {
            using var gamesKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                .OpenSubKey(@"SOFTWARE\GOG.com\Games");

            if (gamesKey == null)
                return games;

            foreach (var gameId in gamesKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = gamesKey.OpenSubKey(gameId);
                    if (gameKey == null) continue;

                    var gameName = gameKey.GetValue("gameName")?.ToString()
                                   ?? gameKey.GetValue("GAMENAME")?.ToString();
                    var gamePath = gameKey.GetValue("path")?.ToString()
                                   ?? gameKey.GetValue("PATH")?.ToString();
                    var exePath = gameKey.GetValue("exe")?.ToString()
                                  ?? gameKey.GetValue("EXE")?.ToString();

                    if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(gamePath))
                        continue;

                    if (!Directory.Exists(gamePath))
                        continue;

                    // GOG 注册表中的 exe 字段已经是完整路径
                    if (!string.IsNullOrEmpty(exePath) && !File.Exists(exePath))
                        exePath = FindMainExe(gamePath);

                    games.Add(new DetectedGame
                    {
                        Name = gameName,
                        InstallPath = gamePath,
                        ExePath = exePath,
                        Source = "GOG"
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GameScanner] 解析 GOG 游戏 {gameId} 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] GOG 扫描异常: {ex.Message}");
        }

        return games;
    }

    #endregion

    #region Ubisoft 扫描

    /// <summary>
    /// 扫描 Ubisoft Connect 已安装的游戏
    /// </summary>
    private List<DetectedGame> ScanUbisoft()
    {
        var games = new List<DetectedGame>();

        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ubisoft Game Launcher", "settings.yaml"
            );

            if (!File.Exists(settingsPath))
                return games;

            // 简单解析 YAML 中的 game_installation_path
            var content = File.ReadAllText(settingsPath);
            var match = System.Text.RegularExpressions.Regex.Match(
                content,
                @"game_installation_path:\s*(.+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (!match.Success)
                return games;

            var installRoot = match.Groups[1].Value.Trim().Trim('"');
            if (!Directory.Exists(installRoot))
                return games;

            // 遍历安装根目录下的子文件夹
            foreach (var dir in Directory.GetDirectories(installRoot))
            {
                var dirName = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(dirName))
                    continue;

                var exePath = FindMainExe(dir);

                games.Add(new DetectedGame
                {
                    Name = dirName,
                    InstallPath = dir,
                    ExePath = exePath,
                    Source = "Ubisoft"
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] Ubisoft 扫描异常: {ex.Message}");
        }

        return games;
    }

    #endregion

    #region EA 扫描

    /// <summary>
    /// 扫描 EA Desktop 已安装的游戏
    /// </summary>
    private List<DetectedGame> ScanEA()
    {
        var games = new List<DetectedGame>();

        try
        {
            var eaDesktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Electronic Arts", "EA Desktop"
            );

            if (!Directory.Exists(eaDesktopPath))
                return games;

            // 查找 user_*.ini 文件
            var iniFiles = Directory.GetFiles(eaDesktopPath, "user_*.ini");
            if (iniFiles.Length == 0)
                return games;

            var iniContent = File.ReadAllText(iniFiles[0]);
            var lines = iniContent.Split('\n');

            foreach (var line in lines)
            {
                if (line.StartsWith("user.downloadinplacedir=", StringComparison.OrdinalIgnoreCase))
                {
                    var downloadPath = line.Split('=', 2)[1].Trim();
                    if (string.IsNullOrEmpty(downloadPath) || !Directory.Exists(downloadPath))
                        continue;

                    // 遍历下载目录下的子文件夹
                    foreach (var dir in Directory.GetDirectories(downloadPath))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(dirName))
                            continue;

                        var exePath = FindMainExe(dir);

                        games.Add(new DetectedGame
                        {
                            Name = dirName,
                            InstallPath = dir,
                            ExePath = exePath,
                            Source = "EA"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] EA 扫描异常: {ex.Message}");
        }

        return games;
    }

    #endregion

    #region Battle.net 扫描

    /// <summary>
    /// 扫描 Battle.net 已安装的游戏
    /// </summary>
    private List<DetectedGame> ScanBattleNet()
    {
        var games = new List<DetectedGame>();

        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battle.net", "Battle.net.config"
            );

            if (!File.Exists(configPath))
                return games;

            var json = File.ReadAllText(configPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            // 获取默认安装路径
            if (doc.RootElement.TryGetProperty("Client", out var client) &&
                client.TryGetProperty("Install", out var install) &&
                install.TryGetProperty("DefaultInstallPath", out var defaultPath))
            {
                var installRoot = defaultPath.GetString();
                if (!string.IsNullOrEmpty(installRoot) && Directory.Exists(installRoot))
                {
                    // 遍历安装目录下的子文件夹
                    foreach (var dir in Directory.GetDirectories(installRoot))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(dirName))
                            continue;

                        var exePath = FindMainExe(dir);

                        games.Add(new DetectedGame
                        {
                            Name = dirName,
                            InstallPath = dir,
                            ExePath = exePath,
                            Source = "Battle.net"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] Battle.net 扫描异常: {ex.Message}");
        }

        return games;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 从注册表读取字符串值
    /// </summary>
    private static string? GetRegistryValue(RegistryHive hive, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在游戏安装目录中启发式查找主 exe 文件
    /// 策略：排除已知非游戏 exe → 在根目录和一级子目录中查找 → 取最大的 exe
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <returns>主 exe 路径，未找到则返回 null</returns>
    private static string? FindMainExe(string installPath)
    {
        try
        {
            // 搜索根目录和一级子目录
            var exeFiles = new List<string>();

            // 根目录
            exeFiles.AddRange(Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly));

            // 一级子目录（常见的 bin、Binaries 等）
            foreach (var subDir in Directory.GetDirectories(installPath))
            {
                var subDirName = Path.GetFileName(subDir)?.ToLowerInvariant() ?? "";
                // 只搜索常见的可执行文件目录
                if (subDirName is "bin" or "binaries" or "binary" or "game" or "x64" or "win64" or "win32")
                {
                    exeFiles.AddRange(Directory.GetFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly));
                }
            }

            if (exeFiles.Count == 0)
                return null;

            // 过滤掉已知非游戏 exe
            var filtered = exeFiles.Where(exeFile =>
            {
                var fileName = Path.GetFileNameWithoutExtension(exeFile) ?? "";
                return !NonGameExePatterns.Any(pattern =>
                    fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            }).ToList();

            if (filtered.Count == 0)
                return null;

            // 取文件体积最大的 exe（通常是游戏主程序）
            return filtered
                .OrderByDescending(f => new FileInfo(f).Length)
                .First();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameScanner] 查找 exe 失败: {installPath}, {ex.Message}");
            return null;
        }
    }

    #endregion
}
