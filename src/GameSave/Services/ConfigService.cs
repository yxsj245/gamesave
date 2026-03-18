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

    /// <summary>是否已显示过首次使用欢迎弹窗</summary>
    public bool HasShownWelcome { get; set; } = false;
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
    /// 缓存的便携版工作目录路径（null 表示未解析，空字符串表示非便携模式）
    /// </summary>
    private static string? _portableWorkDir;

    /// <summary>
    /// 是否为便携版模式
    /// 通过检测命令行参数 --portable-workdir 来判断
    /// </summary>
    public static bool IsPortableMode => !string.IsNullOrEmpty(GetPortableWorkDir());

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

    /// <summary>工作目录路径</summary>
    public string WorkDirectory => _config.WorkDirectory;

    /// <summary>配置文件路径</summary>
    public string ConfigFilePath => _configFilePath;

    /// <summary>当前主题模式</summary>
    public string ThemeMode => _config.ThemeMode;

    /// <summary>是否开机自启动</summary>
    public bool AutoStart => _config.AutoStart;

    /// <summary>是否已显示过首次使用欢迎弹窗</summary>
    public bool HasShownWelcome => _config.HasShownWelcome;

    /// <summary>
    /// 设置主题模式并保存配置
    /// </summary>
    public async Task SetThemeModeAsync(string themeMode)
    {
        _config.ThemeMode = themeMode;
        await SaveConfigAsync();
    }

    /// <summary>
    /// 标记已显示过欢迎弹窗并保存配置
    /// </summary>
    public async Task SetHasShownWelcomeAsync()
    {
        _config.HasShownWelcome = true;
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

        // 便携版模式下 config.json 保存在 AppData 子目录中，与数据一起便携
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

            // 确保实际工作目录存在
            Directory.CreateDirectory(_config.WorkDirectory);
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
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public async Task SaveConfigAsync()
    {
        var json = JsonSerializer.Serialize(_config, AppJsonContext.Default.AppConfig);
        await File.WriteAllTextAsync(_configFilePath, json);
    }

    /// <summary>
    /// 修改工作目录（仅更新配置，不迁移旧内容）
    /// config.json 保持在默认目录，只更新其中的 WorkDirectory 字段
    /// </summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        Directory.CreateDirectory(newPath);
        _config.WorkDirectory = newPath;
        // _configFilePath 不变，始终指向默认目录下的 config.json
        await SaveConfigAsync();
    }

    /// <summary>
    /// 修改工作目录并迁移旧目录内容到新目录
    /// </summary>
    /// <param name="newPath">新的工作目录路径</param>
    /// <param name="progress">进度回调，报告迁移百分比和当前状态文本</param>
    public async Task ChangeWorkDirectoryWithMigrationAsync(string newPath, IProgress<(double percent, string status)>? progress = null)
    {
        var oldPath = _config.WorkDirectory;
        Directory.CreateDirectory(newPath);

        // 收集需要迁移的所有文件（排除 config.json）
        var oldConfigJson = Path.Combine(oldPath, "config.json");
        var filesToCopy = Directory.GetFiles(oldPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.Equals(_configFilePath, StringComparison.OrdinalIgnoreCase)
                     && !f.Equals(oldConfigJson, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filesToCopy.Count > 0)
        {
            for (int i = 0; i < filesToCopy.Count; i++)
            {
                var sourceFile = filesToCopy[i];
                var relativePath = Path.GetRelativePath(oldPath, sourceFile);
                var destFile = Path.Combine(newPath, relativePath);

                // 确保目标子目录存在
                var destDir = Path.GetDirectoryName(destFile);
                if (destDir != null)
                    Directory.CreateDirectory(destDir);

                // 复制文件（在后台线程执行 I/O）
                await Task.Run(() => File.Copy(sourceFile, destFile, overwrite: true));

                // 报告进度
                var percent = (double)(i + 1) / filesToCopy.Count * 100;
                var fileName = Path.GetFileName(sourceFile);
                progress?.Report((percent, $"正在迁移: {relativePath} ({i + 1}/{filesToCopy.Count})"));
            }
        }

        // 更新配置指向新路径（config.json 保持在默认目录）
        _config.WorkDirectory = newPath;
        await SaveConfigAsync();

        // 清理旧工作目录：删除所有数据内容，只保留 config.json
        progress?.Report((99, "正在清理旧工作目录..."));
        await Task.Run(() =>
        {
            // 删除旧目录下的所有子目录
            foreach (var dir in Directory.GetDirectories(oldPath))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }

            // 删除旧目录下除 config.json 外的所有文件
            foreach (var file in Directory.GetFiles(oldPath))
            {
                if (!file.Equals(_configFilePath, StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(file).Equals("config.json", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        });

        progress?.Report((100, "迁移完成"));
    }

    /// <summary>
    /// 获取指定游戏的存档工作子目录
    /// </summary>
    public string GetGameWorkDirectory(string gameId)
    {
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
