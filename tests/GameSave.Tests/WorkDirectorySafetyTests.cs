using System.Reflection;
using System.Text.Json;
using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

/// <summary>所有文件读写均限制在本测试创建的唯一临时目录，不初始化真实用户配置。</summary>
public sealed class WorkDirectorySafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamesave-workdir-test-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Target => Path.Combine(_root, "target");
    private string ConfigPath => Path.Combine(_root, "settings", "config.json");

    [Fact]
    public async Task Migration_CopiesRegisteredGamesAndKeepsEverySourceFile()
    {
        var service = await CreateServiceAsync("game-1");
        var source = WriteSource("game-1", "icon", "图 标.png");
        var save = WriteSource("game-1", "1700000000_手动存档.tar");
        var unrelated = WriteSource("学习资料", "笔记.txt");
        WriteSource("game-1", "_temp_snapshot", "partial.dat");
        WriteSource("config.json");
        var chineseTarget = Path.Combine(_root, "中文 路径", "游戏备份");

        await service.ChangeWorkDirectoryWithMigrationAsync(chineseTarget);

        Assert.Equal(chineseTarget, service.WorkDirectory);
        Assert.Equal("测试内容", await File.ReadAllTextAsync(Path.Combine(chineseTarget, "game-1", "icon", "图 标.png")));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(save));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(Path.Combine(Source, "config.json")));
        Assert.False(Directory.Exists(Path.Combine(chineseTarget, "学习资料")));
        Assert.False(Directory.Exists(Path.Combine(chineseTarget, "game-1", "_temp_snapshot")));
        Assert.False(File.Exists(Path.Combine(chineseTarget, "config.json")));
        Assert.Equal(chineseTarget, ReadPersistedWorkDirectory());
        Assert.Equal(ConfigPath, service.ConfigFilePath);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("trailing")]
    [InlineData("child")]
    [InlineData("parent")]
    [InlineData("dotdot")]
    public async Task Migration_RejectsOverlappingPathsBeforeCreatingOrDeletingAnything(string kind)
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        var destination = kind switch
        {
            "same" => Source,
            "trailing" => Source + Path.DirectorySeparatorChar,
            "child" => Path.Combine(Source, "child"),
            "parent" => _root,
            _ => Path.Combine(Source, "child", "..")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryWithMigrationAsync(destination));

        Assert.Equal(Source, service.WorkDirectory);
        Assert.True(File.Exists(original));
        Assert.False(Directory.Exists(Path.Combine(Source, "child")));
        Assert.Equal(Source, ReadPersistedWorkDirectory());
    }

    [Fact]
    public void EnsureDisjoint_RejectsRootAncestor()
    {
        var root = Path.GetPathRoot(_root)!;
        Assert.Throws<InvalidOperationException>(() => WorkDirectorySafety.EnsureDisjoint(root, _root));
    }

    [Fact]
    public async Task Migration_DoesNotConfuseSiblingPrefixWithChild()
    {
        var service = await CreateServiceAsync("game-1");
        WriteSource("game-1", "save.dat");
        var sibling = Source + "-other";
        await service.ChangeWorkDirectoryWithMigrationAsync(sibling);
        Assert.True(File.Exists(Path.Combine(sibling, "game-1", "save.dat")));
    }

    [Fact]
    public async Task Migration_RejectsExistingGameTargetWithoutOverwriting()
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        var destination = Path.Combine(Target, "game-1", "save.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "用户原有数据");

        await Assert.ThrowsAsync<IOException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target));

        Assert.Equal("用户原有数据", await File.ReadAllTextAsync(destination));
        Assert.True(File.Exists(original));
        Assert.Equal(Source, ReadPersistedWorkDirectory());
    }

    [Fact]
    public async Task Migration_PreservesUnrelatedDestinationFiles()
    {
        var service = await CreateServiceAsync("game-1");
        WriteSource("game-1", "save.dat");
        Directory.CreateDirectory(Target);
        var unrelated = Path.Combine(Target, "笔记.txt");
        await File.WriteAllTextAsync(unrelated, "保留");
        await service.ChangeWorkDirectoryWithMigrationAsync(Target);
        Assert.Equal("保留", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task ChangeWithoutMigration_OnlySwitchesAndNormalizesPath()
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        await service.ChangeWorkDirectoryAsync(Target + Path.DirectorySeparatorChar);
        Assert.Equal(Target, service.WorkDirectory);
        Assert.True(File.Exists(original));
        Assert.Empty(Directory.GetFileSystemEntries(Target));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad.")]
    public async Task Migration_RejectsUnsafeGameIds(string gameId)
    {
        var service = await CreateServiceAsync(gameId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target));
        Assert.False(Directory.Exists(Target));
        Assert.Equal(Source, service.WorkDirectory);
    }

    [Fact]
    public async Task Change_RejectsProtectedRootBeforeCreatingAnything()
    {
        var service = await CreateServiceAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryAsync(Path.GetPathRoot(_root)!));
        Assert.Equal(Source, ReadPersistedWorkDirectory());
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("")]
    public async Task Change_RejectsRelativeAndEmptyPaths(string path)
    {
        var service = await CreateServiceAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeWorkDirectoryAsync(path));
        Assert.Equal(Source, service.WorkDirectory);
    }

    [Fact]
    public async Task Migration_WhenSavingFails_KeepsOldConfigAndSource()
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        // 使用目录充当配置文件目标，稳定制造提交失败，不依赖真实权限或用户环境。
        var invalidConfig = Path.Combine(_root, "invalid-config");
        Directory.CreateDirectory(invalidConfig);
        SetField(service, "_configFilePath", invalidConfig);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target));

        Assert.Equal(Source, service.WorkDirectory);
        Assert.Equal(Source, ReadPersistedWorkDirectory());
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(Target, "game-1", "save.dat")));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Change_WhenSavingFails_RollsBackMemoryAndRetainsPersistedConfig()
    {
        var service = await CreateServiceAsync();
        var invalidConfig = Path.Combine(_root, "invalid-config");
        Directory.CreateDirectory(invalidConfig);
        SetField(service, "_configFilePath", invalidConfig);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangeWorkDirectoryAsync(Target));
        Assert.Equal(Source, service.WorkDirectory);
        Assert.Equal(Source, ReadPersistedWorkDirectory());
    }

    [Fact]
    public async Task Migration_RejectsReentryAndNewStorageOperationsDuringCopy()
    {
        var service = await CreateServiceAsync("game-1");
        WriteSource("game-1", "save.dat");
        var called = false;
        var progress = new InlineProgress(report =>
        {
            if (report.percent >= 100) return;
            called = true;
            Assert.Throws<InvalidOperationException>(() => service.GetGameWorkDirectory("game-1"));
            Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryAsync(Path.Combine(_root, "other"))).GetAwaiter().GetResult();
        });
        await service.ChangeWorkDirectoryWithMigrationAsync(Target, progress);
        Assert.True(called);
        Assert.False(Directory.Exists(Path.Combine(_root, "other")));
        Assert.Equal(Path.Combine(Target, "game-1"), service.GetGameWorkDirectory("game-1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_RejectsSourceChangesBeforeSwitching(bool changeExisting)
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        var changed = false;
        var progress = new InlineProgress(report =>
        {
            if (report.percent >= 100 || changed) return;
            changed = true;
            if (changeExisting) File.WriteAllText(original, "更新后的内容");
            else WriteSource("game-1", "late-save.dat");
        });
        await Assert.ThrowsAsync<IOException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target, progress));
        Assert.Equal(Source, service.WorkDirectory);
        Assert.Equal(Source, ReadPersistedWorkDirectory());
        Assert.True(File.Exists(original));
        Assert.True(changed);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("scheduled")]
    [InlineData("stopping")]
    public async Task Change_RejectsActiveGamesOrScheduledBackups(string state)
    {
        var service = await CreateServiceAsync("game-1");
        var game = service.GetAllGames()[0];
        game.IsRunning = state == "running";
        game.IsScheduledBackupRunning = state == "scheduled";
        game.IsStopping = state == "stopping";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryAsync(Target));
        Assert.False(Directory.Exists(Target));
        game.IsRunning = game.IsScheduledBackupRunning = game.IsStopping = false;
        await service.ChangeWorkDirectoryAsync(Target);
    }

    [Fact]
    public async Task SaveConfig_SerializesConcurrentSavesAndLeavesValidJson()
    {
        var service = await CreateServiceAsync("game-1");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.SaveConfigAsync()));
        Assert.Equal(Source, ReadPersistedWorkDirectory());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!, "*.tmp"));
    }

    [Fact]
    public async Task Migration_RejectsTargetTamperingBeforeCommit()
    {
        var service = await CreateServiceAsync("game-1");
        WriteSource("game-1", "save.dat");
        var progress = new InlineProgress(report =>
        {
            if (report.percent < 100) File.WriteAllText(Path.Combine(Target, "game-1", "save.dat"), "被改变的副本");
        });
        await Assert.ThrowsAsync<IOException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target, progress));
        Assert.Equal(Source, service.WorkDirectory);
        Assert.Equal(Source, ReadPersistedWorkDirectory());
    }

    [Fact]
    public async Task Migration_RejectsDestinationFileWithoutTouchingSource()
    {
        var service = await CreateServiceAsync("game-1");
        var original = WriteSource("game-1", "save.dat");
        await File.WriteAllTextAsync(Target, "不是目录");
        await Assert.ThrowsAnyAsync<IOException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Target));
        Assert.True(File.Exists(original));
        Assert.Equal("不是目录", await File.ReadAllTextAsync(Target));
        Assert.Equal(Source, service.WorkDirectory);
    }

    private async Task<ConfigService> CreateServiceAsync(params string[] gameIds)
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var service = new ConfigService();
        SetField(service, "_config", new AppConfig
        {
            WorkDirectory = Source,
            Games = gameIds.Select(id => new Game { Id = id, Name = "隔离测试游戏" }).ToList()
        });
        SetField(service, "_configFilePath", ConfigPath);
        await service.SaveConfigAsync();
        return service;
    }

    private string WriteSource(params string[] segments)
    {
        var path = Path.Combine(new[] { Source }.Concat(segments).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "测试内容");
        return path;
    }

    private string? ReadPersistedWorkDirectory()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        return json.RootElement.GetProperty("workDirectory").GetString();
    }

    private static void SetField(ConfigService service, string name, object value) =>
        typeof(ConfigService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, value);

    public void Dispose()
    {
        // 只清理构造时生成、位于临时目录中的这个唯一测试根；不接受外部输入路径。
        var fullRoot = Path.GetFullPath(_root);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("gamesave-workdir-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试清理路径不安全。");
        if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
    }

    private sealed class InlineProgress(Action<(double percent, string status)> report) : IProgress<(double percent, string status)>
    {
        public void Report((double percent, string status) value) => report(value);
    }
}