using System.Reflection;
using GameSave.Views;
using Xunit;

namespace GameSave.Tests;

public class PasswordRevealIconTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetPasswordRevealGlyph_UsesSameEyeGlyphForRevealStates(bool isPasswordVisible)
    {
        var method = typeof(SettingsPage).GetMethod(
            "GetPasswordRevealGlyph",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal("\uE890", method.Invoke(null, [isPasswordVisible]));
    }

    [Fact]
    public void GetPasswordRevealIconFontFamily_UsesProjectStandardSystemIconFont()
    {
        var method = typeof(SettingsPage).GetMethod(
            "GetPasswordRevealIconFontFamily",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal("Segoe MDL2 Assets", method.Invoke(null, []));
    }

    [Fact]
    public void GetPasswordRevealIconSize_UsesCompactInlineSize()
    {
        var method = typeof(SettingsPage).GetMethod(
            "GetPasswordRevealIconSize",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(12d, method.Invoke(null, []));
    }

    [Theory]
    [InlineData("GetPasswordRevealButtonWidth", 24d)]
    [InlineData("GetPasswordRevealButtonHeight", 24d)]
    public void PasswordRevealButton_UsesCompactInlineDimensions(string methodName, double expected)
    {
        var method = typeof(SettingsPage).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, []));
    }

    [Fact]
    public void GetPasswordRevealButtonBackgroundResourceKey_CoversTextUnderIcon()
    {
        var method = typeof(SettingsPage).GetMethod(
            "GetPasswordRevealButtonBackgroundResourceKey",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal("TextControlBackgroundFocused", method.Invoke(null, []));
    }

    [Theory]
    [InlineData(false, 0.68d)]
    [InlineData(true, 1d)]
    public void GetPasswordRevealIconOpacity_UsesRevealStateVisual(bool isPasswordVisible, double expected)
    {
        var method = typeof(SettingsPage).GetMethod(
            "GetPasswordRevealIconOpacity",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [isPasswordVisible]));
    }
}
