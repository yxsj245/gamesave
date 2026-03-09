using System.Text.Json;
using System.Text.Json.Serialization;
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
}

/// <summary>
/// 配置管理服务
/// 负责全局配置文件（config.json）的读写和工作目录管理
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private AppConfig _config = new();
    private string _configFilePath = string.Empty;

    /// <summary>工作目录路径</summary>
    public string WorkDirectory => _config.WorkDirectory;

    /// <summary>配置文件路径</summary>
    public string ConfigFilePath => _configFilePath;

    /// <summary>
    /// 获取默认工作目录路径
    /// </summary>
    public static string GetDefaultWorkDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "xingchen", "ycyaw", ".gamesave");
    }

    /// <summary>
    /// 初始化配置服务，加载或创建配置文件
    /// </summary>
    public async Task InitializeAsync()
    {
        var defaultWorkDir = GetDefaultWorkDirectory();

        // 确保工作目录存在
        Directory.CreateDirectory(defaultWorkDir);

        _configFilePath = Path.Combine(defaultWorkDir, "config.json");

        if (File.Exists(_configFilePath))
        {
            // 加载已有配置
            var json = await File.ReadAllTextAsync(_configFilePath);
            _config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
            _config.WorkDirectory = defaultWorkDir;
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
        var json = JsonSerializer.Serialize(_config, _jsonOptions);
        await File.WriteAllTextAsync(_configFilePath, json);
    }

    /// <summary>
    /// 修改工作目录（会迁移配置文件）
    /// </summary>
    public async Task ChangeWorkDirectoryAsync(string newPath)
    {
        Directory.CreateDirectory(newPath);
        _config.WorkDirectory = newPath;
        _configFilePath = Path.Combine(newPath, "config.json");
        await SaveConfigAsync();
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
