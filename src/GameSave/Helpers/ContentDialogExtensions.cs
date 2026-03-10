namespace GameSave.Helpers;

/// <summary>
/// ContentDialog 扩展方法
/// 确保对话框跟随当前应用主题（深色/浅色）
/// </summary>
public static class ContentDialogExtensions
{
    /// <summary>
    /// 显示 ContentDialog 并自动应用当前主题
    /// 替代直接调用 dialog.ShowAsync()，确保深色模式下对话框正确显示
    /// </summary>
    public static async Task<ContentDialogResult> ShowWithThemeAsync(this ContentDialog dialog)
    {
        dialog.RequestedTheme = App.GetCurrentTheme();
        return await dialog.ShowAsync();
    }
}
