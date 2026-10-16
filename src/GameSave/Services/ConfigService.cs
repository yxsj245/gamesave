using System.Text.Json;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 全局配置数据结构（对应 config.json）
/// </summary>
public class AppConfig
{
    /// <summary>工作目录路径</summary>
    public string WorkDirectory { get; set; } = string.Empty;

    /// <summary>已添加的游戏列表</summary>
    public List<Game> Games { get; set; } = new();

    /// <summary>云存储配置列表</summary>
    public List<CloudConfig> CloudConfigs { get; set; } = new();

    /// <summary>
    /// 主题模式：System（跟随系统）、Light（浅色）、Dark（深色）
    /// 默认跟随系统
    /// </summary>
    public string ThemeMode { get; set; } = "System";

    /// <summary>是否开机自启动</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>首页游戏列表展示模式</summary>
    public HomeGameListViewMode HomeGameListViewMode { get; set; } = HomeGameListViewMode.List;

    /// <summary>是否启用游戏内快捷存档快捷键</summary>
    public bool QuickSaveHotkeyEnabled { get; set; } = true;

    /// <summary>游戏内快捷存档快捷键</summary>
    public HotkeyGesture QuickSaveHotkey { get; set; } = HotkeyGesture.Default;
}

/// <summary>
/// 配置管理服务
/// 负责全局配置文件（config.json）的读写和工作目录管理
/// </summary>
public class ConfigService
{
    /// <summary>
    /// 便携版工作目录命令行参数名
    /// </summary>
    private const string PortableWorkDirArg = "--portable-workdir";

    /// <summary>
    /// 通用工作目录命令行参数名（优先级最高）
    /// </summary>
    private const string WorkDirArg = "--workdir";

    /// <summary>
    /// 缓存的便携版工作目录路径（null 表示未解析，空字符串表示非便携模式）
    /// </summary>
    private static string? _portableWorkDir;

    /// <summary>
    /// 缓存的命令行 --workdir 参数路径（null 表示未解析，空字符串表示未指定）
    /// </summary>
    private static string? _commandLineWorkDir;

    /// <summary>
    /// 是否为便携版模式
    /// 通过检测命令行参数 --portable-workdir 来判断
    /// </summary>
    public static bool IsPortableMode => !string.IsNullOrEmpty(GetPortableWorkDir());

    /// <summary>
    /// 工作目录是否由启动参数 --workdir 锁定（锁定时 UI 不可更改）
    /// </summary>
    public static bool IsWorkDirLockedByArgs => !string.IsNullOrEmpty(GetCommandLineWorkDir());

    /// <summary>
    /// 获取命令行 --workdir 参数指定的工作目录路径
    /// 返回空字符串表示未通过 --workdir 参数指定
    /// </summary>
    public static string GetCommandLineWorkDir()
    {
        if (_commandLineWorkDir != null)
            return _commandLineWorkDir;

        _commandLineWorkDir = string.Empty;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(WorkDirArg, StringComparison.OrdinalIgnoreCase))
            {
                _commandLineWorkDir = args[i + 1];
                break;
            }
        }
        return _commandLineWorkDir;
    }

    /// <summary>
    /// 获取便携版的工作目录路径（由启动器通过命令行参数传入）
    /// 返回空字符串表示非便携模式
    /// </summary>
    public static string GetPortableWorkDir()
    {
        if (_portableWorkDir != null)
            return _portableWorkDir;

        _portableWorkDir = string.Empty;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(PortableWorkDirArg, StringComparison.OrdinalIgnoreCase))
            {
                _portableWorkDir = args[i + 1];
                break;
            }
        }
        return _portableWorkDir;
    }

    private AppConfig _config = new();
    private string _configFilePath = string.Empty;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly SemaphoreSlim _workDirectoryChangeLock = new(1, 1);
    private volatile bool _isChangingWorkDirectory;

    /// <summary>工作目录路径</summary>
    public string WorkDirectory => IsWorkDirLockedByArgs ? GetCommandLineWorkDir() : _config.WorkDirectory;

    /// <summary>配置文件路径</summary>
    public string ConfigFilePath => _configFilePath;

    /// <summary>当前主题模式</summary>
    public string ThemeMode => _config.ThemeMode;

    /// <summary>是否开机自启动</summary>
    public bool AutoStart => _config.AutoStart;

    /// <summary>首页游戏列表展示模式</summary>
    public HomeGameListViewMode HomeGameListViewMode => _config.HomeGameListViewMode;

    /// <summary>是否启用游戏内快捷存档快捷键</summary>
    public bool QuickSaveHotkeyEnabled => _config.QuickSaveHotkeyEnabled;

    /// <summary>游戏内快捷存档快捷键</summary>
    public HotkeyGesture QuickSaveHotkey => _config.QuickSaveHotkey;

    /// <summary>
    /// 设置主题模式并保存配置
    /// </summary>
    public async Task SetThemeModeAsync(string themeMode)
    {
        _config.ThemeMode = themeMode;
        await SaveConfigAsync();
    }

    /// <summary>
    /// 设置开机自启动并保存配置
    /// </summary>
    public async Task SetAutoStartAsync(bool autoStart)
    {
        _config.AutoStart = autoStart;
        await SaveConfigAsync();
    }

    /// <summary>
    /// 设置首页游戏列表展示模式并保存配置
    /// </summary>
    public async Task SetHomeGameListViewModeAsync(HomeGameListViewMode viewMode)
    {
        _config.HomeGameListViewMode = viewMode;
        await SaveConfigAsync();
    }

    /// <summary>
    /// 设置游戏内快捷存档快捷键并保存配置。
    /// </summary>
    public async Task SetQuickSaveHotkeyAsync(bool enabled, HotkeyGesture gesture)
    {
        _config.QuickSaveHotkeyEnabled = enabled;
        _config.QuickSaveHotkey = gesture;
        await SaveConfigAsync();
    }

    /// <summary>
    /// 获取默认工作目录路径
    /// 便携版模式下返回启动器传入的工作目录，普通安装模式返回用户目录
    /// </summary>
    public static string GetDefaultWorkDirectory()
    {
        if (IsPortableMode)
        {
            return GetPortableWorkDir();
        }
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "xingchen", "ycyaw", ".gamesave");
    }

    /// <summary>
    /// 初始化配置服务，加载或创建配置文件
    /// config.json 始终保存在默认目录下，WorkDirectory 字段记录实际工作目录位置
    /// </summary>
    public async Task InitializeAsync()
    {
        var defaultWorkDir = GetDefaultWorkDirectory();

        // 确保默认目录存在
        Directory.CreateDirectory(defaultWorkDir);

        // 便携版配置保存在启动器传入的 GameSave-Data 中，不随工作目录切换。
        _configFilePath = Path.Combine(defaultWorkDir, "config.json");

        if (File.Exists(_configFilePath))
        {
            // 加载已有配置
            var json = await File.ReadAllTextAsync(_configFilePath);
            _config = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig) ?? new AppConfig();

            // 如果配置中的 WorkDirectory 为空，则使用默认值
            if (string.IsNullOrWhiteSpace(_config.WorkDirectory))
            {
                _config.WorkDirectory = defaultWorkDir;
                await SaveConfigAsync();
            }

            // 确保实际工作目录存在，如果不可用则回滚到默认目录
            try
            {
                if (!IsWorkDirLockedByArgs)
                    Directory.CreateDirectory(_config.WorkDirectory);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException
                                    || ex is IOException
                                    || ex is UnauthorizedAccessException
                                    || ex is NotSupportedException)
            {
                // 工作目录不可用（例如映射盘符丢失），回滚到默认工作目录
                System.Diagnostics.Debug.WriteLine(
                    $"[ConfigService] 工作目录 '{_config.WorkDirectory}' 不可用（{ex.GetType().Name}: {ex.Message}），已回滚到默认目录: {defaultWorkDir}");
                _config.WorkDirectory = defaultWorkDir;
                Directory.CreateDirectory(defaultWorkDir);
                await SaveConfigAsync();
            }
        }
        else
        {
            // 创建默认配置
            _config = new AppConfig
            {
                WorkDirectory = defaultWorkDir
            };
            await SaveConfigAsync();
        }

        // 启动参数只通过 WorkDirectory 属性覆盖运行时值，不能污染持久配置。
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public async Task SaveConfigAsync()
    {
        await _saveLock.WaitAsync();
        try { await WriteConfigFileAsync(); }
        finally { _saveLock.Release(); }
    }

    private async Task WriteConfigFileAsync()
    {
        string? temporaryPath = null;
        try
        {
            var json = JsonSerializer.Serialize(_config, AppJsonContext.Default.AppConfig);
            // 同目录临时文件写完后再替换，避免直接截断现有配置。
            temporaryPath = _configFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _configFilePath, overwrite: true);
        }
        finally
        {
            if (temporaryPath != null && File.Exists(temporaryPath))
            {
                // 只清理由本次保存生成、位于配置旁的临时文件。
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>仅切换工作目录，保留旧目录中的全部内容。</summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        await EnterWorkDirectoryChangeAsync();
        try
        {
            var target = Helpers.WorkDirectorySafety.NormalizeDestination(newPath);
            Helpers.WorkDirectorySafety.EnsureNoReparsePoints(target);
            Directory.CreateDirectory(target);
            await PersistWorkDirectoryAsync(target);
        }
        finally
        {
            _isChangingWorkDirectory = false;
            _workDirectoryChangeLock.Release();
        }
    }

    /// <summary>
    /// 复制已登记游戏的数据并切换目录；不删除旧副本，不覆盖目标文件。
    /// </summary>
    public async Task ChangeWorkDirectoryWithMigrationAsync(string newPath, IProgress<(double percent, string status)>? progress = null)
    {
        await EnterWorkDirectoryChangeAsync();
        try
        {
            var source = Helpers.WorkDirectorySafety.NormalizePath(WorkDirectory);
            var target = Helpers.WorkDirectorySafety.NormalizeDestination(newPath);
            Helpers.WorkDirectorySafety.EnsureDisjoint(source, target);
            Helpers.WorkDirectorySafety.EnsureNoReparsePoints(source);
            Helpers.WorkDirectorySafety.EnsureNoReparsePoints(target);

            // 只迁移已登记游戏的独立子目录，绝不扫描整棵用户文档目录。
            var directories = new List<string>();
            var files = new List<(string source, string target)>();
            var gameIds = _config.Games.Select(game => game.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var gameId in gameIds)
            {
                Helpers.WorkDirectorySafety.ValidateGameId(gameId);
                var gameSource = Path.Combine(source, gameId);
                var gameTarget = Path.Combine(target, gameId);
                if (!Directory.Exists(gameSource)) continue;
                if (Directory.Exists(gameTarget) || File.Exists(gameTarget))
                    throw new IOException($"目标目录已有游戏数据，为避免覆盖，请选择新的空文件夹：{gameTarget}");
                CollectMigrationFiles(gameSource, gameTarget, directories, files);
            }

            // 预检完成后才创建目标。复制失败会保留旧目录和旧配置，目标可能留有部分副本。
            Directory.CreateDirectory(target);
            foreach (var directory in directories)
            {
                Helpers.WorkDirectorySafety.EnsureNoReparsePoints(directory);
                Directory.CreateDirectory(directory);
            }
            var hashes = new List<byte[]>();
            for (var i = 0; i < files.Count; i++)
            {
                var item = files[i];
                hashes.Add(await Task.Run(() => CopyAndVerifyAsync(item.source, item.target)));
                progress?.Report(((double)(i + 1) / Math.Max(files.Count, 1) * 95,
                    $"正在复制并校验: {Path.GetRelativePath(source, item.source)} ({i + 1}/{files.Count})"));
            }

            // 二次核对清单及内容，防止已取得旧路径的后台任务在复制期间产生新存档。
            var verifiedFiles = new List<(string source, string target)>();
            var verifiedDirectories = new List<string>();
            foreach (var gameId in gameIds)
            {
                var gameSource = Path.Combine(source, gameId);
                if (Directory.Exists(gameSource))
                    CollectMigrationFiles(gameSource, Path.Combine(target, gameId), verifiedDirectories, verifiedFiles);
            }
            if (!gameIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_config.Games.Select(game => game.Id))
                || !files.Select(item => item.source).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(verifiedFiles.Select(item => item.source)))
                throw new IOException("复制期间游戏列表或旧目录内容发生变化，已取消切换，旧目录和配置保持不变。");
            for (var i = 0; i < files.Count; i++)
            {
                Helpers.WorkDirectorySafety.EnsureNoReparsePoints(files[i].source);
                await using var stream = new FileStream(files[i].source, FileMode.Open, FileAccess.Read, FileShare.Read);
                var currentHash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
                Helpers.WorkDirectorySafety.EnsureNoReparsePoints(files[i].target);
                await using var targetStream = new FileStream(files[i].target, FileMode.Open, FileAccess.Read, FileShare.Read);
                var copiedHash = await System.Security.Cryptography.SHA256.HashDataAsync(targetStream);
                if (!currentHash.SequenceEqual(hashes[i]) || !copiedHash.SequenceEqual(hashes[i]))
                    throw new IOException("复制期间旧存档发生变化，已取消切换，请停止备份后重试。");
            }
            await PersistWorkDirectoryAsync(target);
            progress?.Report((100, "复制并切换完成，旧目录全部内容已保留"));
        }
        finally
        {
            _isChangingWorkDirectory = false;
            _workDirectoryChangeLock.Release();
        }
    }

    private async Task EnterWorkDirectoryChangeAsync()
    {
        if (IsWorkDirLockedByArgs)
            throw new InvalidOperationException("工作目录由启动参数指定，无法更改。");
        if (!await _workDirectoryChangeLock.WaitAsync(0))
            throw new InvalidOperationException("工作目录正在更改，请等待当前操作完成。");
        _isChangingWorkDirectory = true;
        if (_config.Games.Any(game => game.IsRunning || game.IsStopping || game.IsScheduledBackupRunning))
        {
            _isChangingWorkDirectory = false;
            _workDirectoryChangeLock.Release();
            throw new InvalidOperationException("请先退出游戏并停止定时备份，再更改工作目录。");
        }
    }

    private async Task PersistWorkDirectoryAsync(string target)
    {
        // 切换和回滚都在保存锁内，避免其它设置把未提交的新目录提前写入配置。
        await _saveLock.WaitAsync();
        var previous = _config.WorkDirectory;
        try
        {
            _config.WorkDirectory = target;
            await WriteConfigFileAsync();
        }
        catch
        {
            _config.WorkDirectory = previous;
            throw;
        }
        finally { _saveLock.Release(); }
    }

    private static void CollectMigrationFiles(string source, string target,
        List<string> directories, List<(string source, string target)> files)
    {
        Helpers.WorkDirectorySafety.EnsureNoReparsePoints(source);
        directories.Add(target);
        foreach (var file in Directory.GetFiles(source))
        {
            Helpers.WorkDirectorySafety.EnsureNoReparsePoints(file);
            files.Add((file, Path.Combine(target, Path.GetFileName(file))));
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            // 临时快照不是完成的备份，不能作为正式存档迁移。
            if (Path.GetFileName(directory).Equals("_temp_snapshot", StringComparison.OrdinalIgnoreCase)) continue;
            CollectMigrationFiles(directory, Path.Combine(target, Path.GetFileName(directory)), directories, files);
        }
    }

    private static async Task<byte[]> CopyAndVerifyAsync(string source, string target)
    {
        Helpers.WorkDirectorySafety.EnsureNoReparsePoints(source);
        Helpers.WorkDirectorySafety.EnsureNoReparsePoints(target);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await input.CopyToAsync(output);
        await output.FlushAsync();
        input.Position = 0;
        output.Position = 0;
        var sourceHash = await System.Security.Cryptography.SHA256.HashDataAsync(input);
        var targetHash = await System.Security.Cryptography.SHA256.HashDataAsync(output);
        if (!sourceHash.SequenceEqual(targetHash))
            throw new IOException($"复制校验失败，旧目录未改动：{source}");
        return sourceHash;
    }

    /// <summary>
    /// 获取指定游戏的存档工作子目录
    /// </summary>
    public string GetGameWorkDirectory(string gameId)
    {
        if (_isChangingWorkDirectory)
            throw new InvalidOperationException("正在更改工作目录，请等待完成后再操作存档。");
        Helpers.WorkDirectorySafety.ValidateGameId(gameId);
        var dir = Path.Combine(WorkDirectory, gameId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    #region 游戏管理

    /// <summary>获取所有游戏</summary>
    public List<Game> GetAllGames() => _config.Games;

    /// <summary>根据 ID 获取游戏</summary>
    public Game? GetGameById(string id) => _config.Games.FirstOrDefault(g => g.Id == id);

    /// <summary>添加游戏</summary>
    public async Task AddGameAsync(Game game)
    {
        _config.Games.Add(game);
        await SaveConfigAsync();
    }

    /// <summary>更新游戏信息</summary>
    public async Task UpdateGameAsync(Game game)
    {
        var index = _config.Games.FindIndex(g => g.Id == game.Id);
        if (index >= 0)
        {
            _config.Games[index] = game;
            await SaveConfigAsync();
        }
    }

    /// <summary>删除游戏</summary>
    public async Task RemoveGameAsync(string gameId)
    {
        _config.Games.RemoveAll(g => g.Id == gameId);
        await SaveConfigAsync();
    }

    /// <summary>重新排列游戏列表顺序（用于拖拽排序后持久化）</summary>
    public async Task ReorderGamesAsync(List<string> orderedGameIds)
    {
        // 按传入的 ID 顺序重新设置 SortOrder
        for (int i = 0; i < orderedGameIds.Count; i++)
        {
            var game = _config.Games.FirstOrDefault(g => g.Id == orderedGameIds[i]);
            if (game != null)
            {
                game.SortOrder = i;
            }
        }

        // 按 SortOrder 重新排列内存中的游戏列表
        _config.Games = _config.Games.OrderBy(g => g.SortOrder).ToList();
        await SaveConfigAsync();
    }

    #endregion

    #region 云配置管理

    /// <summary>获取所有云存储配置</summary>
    public List<CloudConfig> GetAllCloudConfigs() => _config.CloudConfigs;

    /// <summary>根据 ID 获取云存储配置</summary>
    public CloudConfig? GetCloudConfigById(string id) => _config.CloudConfigs.FirstOrDefault(c => c.Id == id);

    /// <summary>添加云存储配置</summary>
    public async Task AddCloudConfigAsync(CloudConfig config)
    {
        _config.CloudConfigs.Add(config);
        await SaveConfigAsync();
    }

    /// <summary>更新云存储配置</summary>
    public async Task UpdateCloudConfigAsync(CloudConfig config)
    {
        var index = _config.CloudConfigs.FindIndex(c => c.Id == config.Id);
        if (index >= 0)
        {
            _config.CloudConfigs[index] = config;
            await SaveConfigAsync();
        }
    }

    /// <summary>删除云存储配置</summary>
    public async Task RemoveCloudConfigAsync(string configId)
    {
        _config.CloudConfigs.RemoveAll(c => c.Id == configId);
        await SaveConfigAsync();
    }

    #endregion
}
