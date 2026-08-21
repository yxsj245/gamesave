using System.Reflection;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

public class QuickSaveServiceTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "gamesave-quick-save-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_WithoutRunningGame_ReturnsNoRunningGame()
    {
        var configService = CreateConfigService([]);
        var gameService = new FakeQuickSaveGameService();
        var notifications = new List<(string Title, string Message)>();
        var service = new QuickSaveService(configService, gameService, (title, message) => notifications.Add((title, message)));

        var result = await service.ExecuteAsync();

        Assert.Equal(QuickSaveResultKind.NoRunningGame, result.Kind);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task ExecuteAsync_WithRunningGame_CreatesTimeNamedSaveAndNotifies()
    {
        var game = new Game { Id = "game-1", Name = "测试游戏" };
        var configService = CreateConfigService([game]);
        var gameService = new FakeQuickSaveGameService
        {
            IsGameRunning = true,
            RunningGameId = game.Id
        };
        gameService.BackupHandler = (_, time) => Task.FromResult(new SaveFile
        {
            GameId = game.Id,
            Name = GameService.CreateQuickSaveName(time),
            Path = "quick-save.tar"
        });
        var notifications = new List<(string Title, string Message)>();
        var service = new QuickSaveService(configService, gameService, (title, message) => notifications.Add((title, message)));
        var backupTime = new DateTimeOffset(2026, 8, 21, 14, 35, 42, TimeSpan.FromHours(8));

        var result = await service.ExecuteAsync(backupTime);

        Assert.Equal(QuickSaveResultKind.Success, result.Kind);
        Assert.Equal("快捷存档 2026-08-21 14-35-42", result.SaveFile?.Name);
        Assert.Contains("快捷存档成功", Assert.Single(notifications).Title);
    }

    [Fact]
    public async Task ExecuteAsync_WhilePreviousSaveIsRunning_ReturnsBusy()
    {
        var game = new Game { Id = "game-1", Name = "测试游戏" };
        var configService = CreateConfigService([game]);
        var releaseBackup = new TaskCompletionSource<SaveFile>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameService = new FakeQuickSaveGameService
        {
            IsGameRunning = true,
            RunningGameId = game.Id,
            BackupHandler = (_, _) => releaseBackup.Task
        };
        var service = new QuickSaveService(configService, gameService, (_, _) => { });

        var firstExecution = service.ExecuteAsync();
        var secondResult = await service.ExecuteAsync();
        releaseBackup.SetResult(new SaveFile { GameId = game.Id, Name = "快捷存档" });
        var firstResult = await firstExecution;

        Assert.Equal(QuickSaveResultKind.Busy, secondResult.Kind);
        Assert.Equal(QuickSaveResultKind.Success, firstResult.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBackupFails_ReturnsFailedAndAllowsRetry()
    {
        var game = new Game { Id = "game-1", Name = "测试游戏" };
        var configService = CreateConfigService([game]);
        var gameService = new FakeQuickSaveGameService
        {
            IsGameRunning = true,
            RunningGameId = game.Id,
            BackupHandler = (_, _) => throw new IOException("磁盘写入失败")
        };
        var service = new QuickSaveService(configService, gameService, (_, _) => { });

        var firstResult = await service.ExecuteAsync();
        var secondResult = await service.ExecuteAsync();

        Assert.Equal(QuickSaveResultKind.Failed, firstResult.Kind);
        Assert.Equal(QuickSaveResultKind.Failed, secondResult.Kind);
    }

    private ConfigService CreateConfigService(List<Game> games)
    {
        Directory.CreateDirectory(_workDirectory);
        var configService = new ConfigService();
        var config = new AppConfig
        {
            WorkDirectory = _workDirectory,
            Games = games
        };

        typeof(ConfigService).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, config);
        typeof(ConfigService).GetField("_configFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, Path.Combine(_workDirectory, "config.json"));
        return configService;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
            Directory.Delete(_workDirectory, recursive: true);
    }

    private sealed class FakeQuickSaveGameService : IQuickSaveGameService
    {
        public bool IsGameRunning { get; set; }
        public string? RunningGameId { get; set; }
        public Func<Game, DateTimeOffset, Task<SaveFile>> BackupHandler { get; set; } =
            (_, _) => Task.FromResult(new SaveFile());

        public Task<SaveFile> QuickBackupAsync(Game game, DateTimeOffset backupTime) =>
            BackupHandler(game, backupTime);
    }
}
