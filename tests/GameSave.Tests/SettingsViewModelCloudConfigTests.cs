using System.Reflection;
using GameSave.Models;
using GameSave.Services;
using GameSave.ViewModels;
using Xunit;

namespace GameSave.Tests;

public class SettingsViewModelCloudConfigTests
{
    [Fact]
    public void GetDefaultRemoteBasePath_ForWebDav_ReturnsPortableGameSavePath()
    {
        Assert.Equal("GameSave", SettingsViewModel.GetDefaultRemoteBasePath(CloudProviderType.WebDav));
    }

    [Fact]
    public async Task SaveCloudConfigAsync_WhenEditing_UpdatesExistingConfigInsteadOfAdding()
    {
        var existing = new CloudConfig
        {
            Id = "webdav-nas-gamedata",
            DisplayName = "Old WebDAV",
            ProviderType = CloudProviderType.WebDav,
            ServerUrl = "http://old.example.test",
            Username = "old-user",
            Password = "old-password",
            RemoteBasePath = "old/path",
            IsEnabled = true
        };
        var configService = CreateConfigService([existing]);
        var viewModel = new SettingsViewModel(configService);
        viewModel.LoadCloudConfigs();

        viewModel.BeginEditCloudConfig(existing);
        viewModel.NewDisplayName = "Updated WebDAV";
        viewModel.NewServerUrl = "http://192.168.1.99:5005";
        viewModel.NewUsername = "admin";
        viewModel.NewPassword = "new-password";
        viewModel.NewRemoteBasePath = "GameSave";

        var (success, message) = await viewModel.SaveCloudConfigAsync();

        Assert.True(success, message);
        var savedConfigs = configService.GetAllCloudConfigs();
        var saved = Assert.Single(savedConfigs);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal("Updated WebDAV", saved.DisplayName);
        Assert.Equal("http://192.168.1.99:5005", saved.ServerUrl);
        Assert.Equal("admin", saved.Username);
        Assert.Equal("new-password", saved.Password);
        Assert.Equal("GameSave", saved.RemoteBasePath);
        Assert.Single(viewModel.CloudConfigs);
        Assert.False(viewModel.IsEditingCloudConfig);
    }

    private static ConfigService CreateConfigService(List<CloudConfig> cloudConfigs)
    {
        var configService = new ConfigService();
        var config = new AppConfig
        {
            WorkDirectory = Path.Combine(Path.GetTempPath(), "gamesave-settings-test-" + Guid.NewGuid().ToString("N")),
            CloudConfigs = cloudConfigs
        };

        var type = typeof(ConfigService);
        type.GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, config);
        type.GetField("_configFilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(configService, Path.Combine(config.WorkDirectory, "config.json"));
        Directory.CreateDirectory(config.WorkDirectory);

        return configService;
    }
}
