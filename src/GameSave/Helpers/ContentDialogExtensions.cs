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

        // 自动为所有的 ContentDialog 内部的 Panel 表单组件添加统一的入场动画
        if (dialog.Content is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            AddTransitionsToPanel(panel);
        }
        else if (dialog.Content is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer && scrollViewer.Content is Microsoft.UI.Xaml.Controls.Panel scrollPanel)
        {
            AddTransitionsToPanel(scrollPanel);
        }

        return await dialog.ShowAsync();
    }

    private static void AddTransitionsToPanel(Microsoft.UI.Xaml.Controls.Panel panel)
    {
        if (panel.ChildrenTransitions == null)
        {
            panel.ChildrenTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        }

        bool hasEntrance = false;
        foreach (var t in panel.ChildrenTransitions)
        {
            if (t is Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition)
            {
                hasEntrance = true;
                break;
            }
        }

        if (!hasEntrance)
        {
            panel.ChildrenTransitions.Add(new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { IsStaggeringEnabled = true });
        }
    }
}
