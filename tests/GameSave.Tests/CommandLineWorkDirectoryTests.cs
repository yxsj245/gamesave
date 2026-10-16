using System.Reflection;
using System.Text.Json;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

[CollectionDefinition("CommandLineWorkDirectory", DisableParallelization = true)]
public sealed class CommandLineWorkDirectoryCollection { }

/// <summary>串行修改参数缓存后立即恢复，不运行真实程序或初始化默认目录。</summary>
[Collection("CommandLineWorkDirectory")]
public sealed class CommandLineWorkDirectoryTests
{
    [Fact]
    public async Task ExplicitWorkDirectory_IsRuntimeOnlyAndCannotBeChangedByService()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamesave-args-test-" + Guid.NewGuid().ToString("N"));
        var argsField = typeof(ConfigService).GetField("_commandLineWorkDir", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = argsField.GetValue(null);
        Directory.CreateDirectory(root);
        try
        {
            var storedPath = Path.Combine(root, "stored");
            var runtimePath = Path.Combine(root, "runtime");
            var configPath = Path.Combine(root, "config.json");
            var service = new ConfigService();
            typeof(ConfigService).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, new AppConfig { WorkDirectory = storedPath });
            typeof(ConfigService).GetField("_configFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, configPath);
            argsField.SetValue(null, runtimePath);

            await service.SetThemeModeAsync("Dark");

            Assert.Equal(runtimePath, service.WorkDirectory);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            Assert.Equal(storedPath, json.RootElement.GetProperty("workDirectory").GetString());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryAsync(Path.Combine(root, "new")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkDirectoryWithMigrationAsync(Path.Combine(root, "new")));
            Assert.False(Directory.Exists(Path.Combine(root, "new")));
        }
        finally
        {
            argsField.SetValue(null, previous);
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith("gamesave-args-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("测试清理路径不安全。");
            if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, recursive: true);
        }
    }
}