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

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // 默认导航到主页
            ContentFrame.Navigate(typeof(HomePage));
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
                    case GameRunStatus.Running:
                        // 游戏运行中 — 显示状态栏, 更新侧边栏
                        StatusBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        StatusBarIcon.Glyph = "\uE768"; // Play 图标
                        StatusBarIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.LimeGreen);
                        StatusBarText.Text = $"{e.GameName} 运行中";
                        StatusBarDetail.Text = $"PID: {e.ProcessId}";
                        StatusBarProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                        // 更新侧边栏状态
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

                        // 恢复窗口显示
                        RestoreWindow();
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
