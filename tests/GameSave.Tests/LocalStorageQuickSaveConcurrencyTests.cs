using System.Reflection;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

public class LocalStorageQuickSaveConcurrencyTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "gamesave-concurrency-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BackupSaveAsync_ForSameGame_SerializesHotBackups()
    {
        var sourceDirectory = Path.Combine(_rootDirectory, "source");
        var workDirectory = Path.Combine(_rootDirectory, "work");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "save.dat"), new string('x', 1024 * 64));

        var configService = CreateConfigService(workDirectory);
        var storageService = new LocalStorageService(configService);
        var game = new Game
        {
            Id = "game-1",
            Name = "测试游戏",
            SaveFolderPaths = [sourceDirectory]
        };

        var saves = await Task.WhenAll(
            storageService.BackupSaveAsync(game, "快捷存档 A", useHotBackup: true),
            storageService.BackupSaveAsync(game, "快捷存档 B", useHotBackup: true));

        Assert.Equal(2, saves.Length);
        Assert.All(saves, save => Assert.True(File.Exists(save.Path), save.Path));
        Assert.False(Directory.Exists(Path.Combine(workDirectory, game.Id, "_temp_snapshot")));
    }

    private static ConfigService CreateConfigService(string workDirectory)
    {
        var configService = new ConfigService();
        var config = new AppConfig { WorkDirectory = workDirectory };
        typeof(ConfigService).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, config);
        typeof(ConfigService).GetField("_configFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, Path.Combine(workDirectory, "config.json"));
        return configService;
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }
}
