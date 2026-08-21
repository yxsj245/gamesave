using System.Reflection;
using GameSave.Models;
using GameSave.Services;
using GameSave.ViewModels;
using Xunit;

namespace GameSave.Tests;

public class QuickSaveHotkeySettingsTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "gamesave-hotkey-settings-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyQuickSaveHotkeyAsync_WhenRegistrationSucceeds_PersistsNewGesture()
    {
        var configService = CreateConfigService();
        var registrar = new FakeGlobalHotkeyService { RegistrationSucceeds = true };
        var viewModel = new SettingsViewModel(configService, registrar);
        viewModel.LoadSettings();
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47);

        var result = await viewModel.ApplyQuickSaveHotkeyAsync(true, gesture);

        Assert.True(result.success, result.message);
        Assert.Equal(gesture, configService.QuickSaveHotkey);
        Assert.Equal(gesture, viewModel.QuickSaveHotkey);
    }

    [Fact]
    public async Task ApplyQuickSaveHotkeyAsync_WhenRegistrationFails_KeepsOldConfig()
    {
        var configService = CreateConfigService();
        var registrar = new FakeGlobalHotkeyService
        {
            RegistrationSucceeds = false,
            FailureMessage = "快捷键已被占用"
        };
        var viewModel = new SettingsViewModel(configService, registrar);
        viewModel.LoadSettings();
        var originalGesture = configService.QuickSaveHotkey;
        var newGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47);

        var result = await viewModel.ApplyQuickSaveHotkeyAsync(true, newGesture);

        Assert.False(result.success);
        Assert.Equal(originalGesture, configService.QuickSaveHotkey);
        Assert.Equal(originalGesture, viewModel.QuickSaveHotkey);
        Assert.True(viewModel.IsQuickSaveHotkeyStatusError);
    }

    private ConfigService CreateConfigService()
    {
        Directory.CreateDirectory(_workDirectory);
        var configService = new ConfigService();
        var config = new AppConfig { WorkDirectory = _workDirectory };

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

    private sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
    {
        public bool RegistrationSucceeds { get; set; }
        public string FailureMessage { get; set; } = "注册失败";
        public bool IsRegistered { get; private set; }
        public HotkeyGesture? RegisteredGesture { get; private set; }
        public string StatusMessage { get; private set; } = "快捷键尚未注册";

        public bool TryUpdateRegistration(bool enabled, HotkeyGesture gesture, out string message)
        {
            if (!RegistrationSucceeds)
            {
                message = FailureMessage;
                StatusMessage = message;
                return false;
            }

            RegisteredGesture = gesture;
            IsRegistered = enabled;
            message = enabled ? $"已注册：{gesture.ToDisplayString()}" : "快捷存档快捷键已关闭";
            StatusMessage = message;
            return true;
        }
    }
}
