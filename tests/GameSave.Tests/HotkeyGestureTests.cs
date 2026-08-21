using System.Text.Json;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

public class HotkeyGestureTests
{
    [Fact]
    public void DefaultGesture_IsValidAndReadable()
    {
        var gesture = HotkeyGesture.Default;

        Assert.True(gesture.IsValid(out var message), message);
        Assert.Equal("Ctrl+Shift+F5", gesture.ToDisplayString());
    }

    [Theory]
    [InlineData(0, 0x41)]
    [InlineData((uint)HotkeyModifiers.Windows, 0x41)]
    [InlineData((uint)HotkeyModifiers.Control, 0x7B)]
    [InlineData((uint)HotkeyModifiers.Control, 0x2C)]
    public void InvalidGesture_IsRejected(uint modifiers, int virtualKey)
    {
        var gesture = new HotkeyGesture((HotkeyModifiers)modifiers, virtualKey);

        Assert.False(gesture.IsValid(out var message));
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void OldConfigWithoutHotkeyFields_UsesDefaults()
    {
        const string json = """
            {
              "workDirectory": "C:\\GameSave",
              "games": [],
              "cloudConfigs": []
            }
            """;

        var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);

        Assert.NotNull(config);
        Assert.True(config.QuickSaveHotkeyEnabled);
        Assert.Equal(HotkeyGesture.Default, config.QuickSaveHotkey);
    }

    [Fact]
    public void HotkeyConfig_RoundTripsThroughSourceGeneratedJson()
    {
        var expectedGesture = new HotkeyGesture(HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x31);
        var config = new AppConfig
        {
            WorkDirectory = "C:\\GameSave",
            QuickSaveHotkeyEnabled = false,
            QuickSaveHotkey = expectedGesture
        };

        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);

        Assert.NotNull(restored);
        Assert.False(restored.QuickSaveHotkeyEnabled);
        Assert.Equal(expectedGesture, restored.QuickSaveHotkey);
    }
}
