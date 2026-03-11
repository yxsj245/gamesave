using GameSave.Services;
using System.Runtime.InteropServices;

namespace GameSave.Views
{
    /// <summary>
    /// 主页面 - 导航壳 + 全局状态栏
    /// </summary>
    public partial class MainPage : Page
    {
        // Win32 API 用于窗口最小化/恢复
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        public MainPage()
        {
            this.InitializeComponent();

            // 订阅游戏状态变更事件
            App.GameService.StatusChanged += GameService_StatusChanged;
        }

        private async void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // 默认导航到主页
            ContentFrame.Navigate(typeof(HomePage));

            // 检查是否需要显示首次使用欢迎弹窗
            await ShowWelcomeDialogIfFirstLaunchAsync();
        }

        /// <summary>
        /// 首次使用时显示欢迎弹窗
        /// </summary>
        private async Task ShowWelcomeDialogIfFirstLaunchAsync()
        {
            if (App.ConfigService.HasShownWelcome)
                return;

            var dialog = new ContentDialog
            {
                Title = "欢迎使用 GSAM 游戏存档管理器（公测）",
                XamlRoot = this.XamlRoot,
                RequestedTheme = App.GetCurrentTheme(),
                PrimaryButtonText = "我知道了",
                DefaultButton = ContentDialogButton.Primary
            };

            // 构建弹窗内容
            var contentPanel = new StackPanel { Spacing = 12 };

            contentPanel.Children.Add(new TextBlock
            {
                Text = "📢 公测须知",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = "当前版本为公开测试版本，所有功能和界面均不代表最终效果。我们正在积极开发和优化中，部分功能可能存在不完善之处，敬请谅解。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray)
            });

            // 分隔线
            contentPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Thickness(0, 4, 0, 4)
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = "💬 交流与反馈",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = "公测期间欢迎各位大佬提出宝贵的意见和建议，你们的建议将直接决定后续功能的走向！",
                TextWrapping = TextWrapping.Wrap
            });

            // QQ 联系方式
            var qqPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };

            var qqPersonal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            qqPersonal.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 14,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.DodgerBlue)
            });
            qqPersonal.Children.Add(new TextBlock
            {
                Text = "QQ：3354416548",
                IsTextSelectionEnabled = true,
                FontSize = 14
            });
            qqPanel.Children.Add(qqPersonal);

            var qqGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            qqGroup.Children.Add(new FontIcon
            {
                Glyph = "\uE716",
                FontSize = 14,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.MediumPurple)
            });
            qqGroup.Children.Add(new TextBlock
            {
                Text = "QQ群：1053482216",
                IsTextSelectionEnabled = true,
                FontSize = 14
            });
            qqPanel.Children.Add(qqGroup);

            contentPanel.Children.Add(qqPanel);

            // 底部感谢文字
            contentPanel.Children.Add(new TextBlock
            {
                Text = "感谢您的使用与支持！🎮",
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            dialog.Content = contentPanel;

            await dialog.ShowAsync();

            // 标记已显示欢迎弹窗
            await App.ConfigService.SetHasShownWelcomeAsync();
        }

        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            // 侧边栏展开时，为内容区域添加平滑过渡动画
            ApplyContentTransition();
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            // 侧边栏收起时，为内容区域添加平滑过渡动画
            ApplyContentTransition();
        }

        /// <summary>
        /// 为内容区域应用平滑的布局过渡动画
        /// </summary>
        private void ApplyContentTransition()
        {
            // 使用 RepositionThemeTransition 实现平滑的位移过渡
            ContentFrame.Transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection
            {
                new Microsoft.UI.Xaml.Media.Animation.RepositionThemeTransition()
            };

            // 动画完成后移除 Transitions，避免其他场景不必要的动画
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                ContentFrame.Transitions = null;
                timer.Stop();
            };
            timer.Start();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                switch (tag)
                {
                    case "Home":
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                    case "Cloud":
                        ContentFrame.Navigate(typeof(CloudPage));
                        break;
                }
            }
        }

        /// <summary>
        /// 处理游戏状态变更事件，更新底部状态栏和侧边栏状态指示
        /// </summary>
        private void GameService_StatusChanged(object? sender, GameStatusInfo e)
        {
            // 需要回到 UI 线程
            DispatcherQueue.TryEnqueue(() =>
            {
                switch (e.Status)
                {
                    case GameRunStatus.Launching:
                        // 启动进度已迁移到游戏列表项中显示，底部状态栏不再处理
                        // 仅更新侧边栏状态指示
                        StatusNavItem.Content = $"正在启动 {e.GameName}...";
                        StatusIcon.Glyph = "\uE7FC";
                        break;

                    case GameRunStatus.Running:
                        // 运行状态已通过列表项的停止按钮体现，底部状态栏不再显示
                        // 仅更新侧边栏状态指示
                        StatusNavItem.Content = $"{e.GameName} 运行中";
                        StatusIcon.Glyph = "\uE768";
                        break;

                    case GameRunStatus.BackingUp:
                        // 正在备份 — 显示进度指示器
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        StatusBarIcon.Glyph = "\uE898"; // 备份图标
                        StatusBarIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.Orange);
                        StatusBarText.Text = $"正在备份 {e.GameName} 的存档...";
                        StatusBarDetail.Text = "请勿关闭软件";
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        if (e.Progress.HasValue)
                        {
                            StatusBarProgress.IsIndeterminate = false;
                            StatusBarProgress.Value = e.Progress.Value;
                        }
                        else
                        {
                            StatusBarProgress.IsIndeterminate = true;
                        }

                        StatusNavItem.Content = "正在备份...";
                        StatusIcon.Glyph = "\uE898";

                        // 游戏退出后备份时不再恢复窗口，保持后台运行
                        break;

                    case GameRunStatus.Restoring:
                        // 正在恢复 — 显示进度指示器
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        StatusBarIcon.Glyph = "\uE896"; // 恢复/下载图标
                        StatusBarIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.DodgerBlue);
                        StatusBarText.Text = e.Message;
                        StatusBarDetail.Text = "请勿关闭软件";
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        if (e.Progress.HasValue)
                        {
                            StatusBarProgress.IsIndeterminate = false;
                            StatusBarProgress.Value = e.Progress.Value;
                        }
                        else
                        {
                            StatusBarProgress.IsIndeterminate = true;
                        }

                        StatusNavItem.Content = "正在恢复...";
                        StatusIcon.Glyph = "\uE896";

                        // 恢复窗口显示
                        RestoreWindow();
                        break;

                    case GameRunStatus.Uploading:
                        // 正在上传到云端 — 显示上传进度
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        StatusBarIcon.Glyph = "\uE753"; // 云端图标
                        StatusBarIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.DodgerBlue);
                        StatusBarText.Text = $"正在上传 {e.GameName} 存档到云端...";
                        StatusBarDetail.Text = "请勿关闭软件";
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        if (e.Progress.HasValue)
                        {
                            StatusBarProgress.IsIndeterminate = false;
                            StatusBarProgress.Value = e.Progress.Value;
                        }
                        else
                        {
                            StatusBarProgress.IsIndeterminate = true;
                        }

                        StatusNavItem.Content = "正在上传...";
                        StatusIcon.Glyph = "\uE753";
                        break;

                    case GameRunStatus.Completed:
                        // 备份完成 — 显示完成状态
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        StatusBarIcon.Glyph = "\uE73E"; // 对勾图标
                        StatusBarIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.LimeGreen);
                        StatusBarText.Text = $"{e.GameName} 存档备份完成";
                        StatusBarDetail.Text = "";
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                        StatusNavItem.Content = "备份完成";
                        StatusIcon.Glyph = "\uE73E";
                        break;

                    case GameRunStatus.Idle:
                    default:
                        // 空闲状态 — 隐藏状态栏
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                        StatusNavItem.Content = "同步就绪";
                        StatusIcon.Glyph = "\uE895";
                        break;
                }
            });
        }

        /// <summary>
        /// 最小化主窗口
        /// </summary>
        public static void MinimizeWindow()
        {
            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                ShowWindow(hwnd, SW_MINIMIZE);
            }
        }

        /// <summary>
        /// 恢复主窗口显示（支持从最小化和托盘隐藏状态恢复）
        /// </summary>
        public static void RestoreWindow()
        {
            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                ShowWindow(hwnd, SW_SHOW);    // 先确保窗口可见（从隐藏状态恢复）
                ShowWindow(hwnd, SW_RESTORE); // 从最小化状态恢复正常大小
                SetForegroundWindow(hwnd);    // 激活窗口到前台
            }
        }
    }
}
