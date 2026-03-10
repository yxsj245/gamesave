using GameSave.Helpers;
using GameSave.Services;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.InteropServices;

namespace GameSave
{
    /// <summary>
    /// 应用程序入口
    /// </summary>
    public partial class App : Application
    {
        // Win32 API 用于窗口显示/隐藏
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        private Window? m_window;
        private TrayIconHelper? _trayIcon;

        /// <summary>
        /// 标记是否真正退出应用（而非最小化到托盘）
        /// </summary>
        private bool _isExiting = false;

        /// <summary>
        /// 标记是否首次最小化到托盘（用于只显示一次气泡提示）
        /// </summary>
        private bool _firstMinimizeToTray = true;

        // 全局服务实例
        public static ConfigService ConfigService { get; } = new();
        public static ProcessMonitorService ProcessMonitorService { get; } = new();
        public static LocalStorageService LocalStorageService { get; private set; } = null!;
        public static GameService GameService { get; private set; } = null!;
        public static ScheduledBackupService ScheduledBackupService { get; private set; } = null!;

        public App()
        {
            this.InitializeComponent();

            // 初始化服务依赖链
            LocalStorageService = new LocalStorageService(ConfigService);
            GameService = new GameService(ConfigService, LocalStorageService, ProcessMonitorService);
            ScheduledBackupService = new ScheduledBackupService(GameService, LocalStorageService, ConfigService);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var logPath = Path.Combine(ConfigService.GetDefaultWorkDirectory(), "crash.log");
                try { File.WriteAllText(logPath, $"[{DateTime.Now}]\n{e.ExceptionObject}"); } catch { }
            };

            Microsoft.UI.Xaml.Application.Current.UnhandledException += (s, e) =>
            {
                var logPath = Path.Combine(ConfigService.GetDefaultWorkDirectory(), "xaml_crash.log");
                try { File.WriteAllText(logPath, $"[{DateTime.Now}]\n{e.Exception}"); } catch { }
                e.Handled = true;
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // 初始化配置服务（加载主题设置等）
            await ConfigService.InitializeAsync();

            // 检测是否为静默启动模式（开机自启时使用）
            bool isSilentStart = Services.AutoStartService.IsSilentStart();

            m_window = new Window();
            m_window.Title = "GameSave Manager";

            // 设置窗口标题栏图标
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("Assets/app.ico");

            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            m_window.Content = rootFrame;
            rootFrame.Navigate(typeof(Views.MainPage), e.Arguments);

            // 应用已保存的主题设置
            ApplyTheme(ConfigService.ThemeMode);

#if !DEBUG
            // 仅在发布模式下启用系统托盘（开发环境下关闭窗口直接退出，方便调试）
            InitializeTrayIcon();
            m_window.Closed += MainWindow_Closed;

            if (isSilentStart)
            {
                // 静默模式：先激活窗口确保 XAML 初始化完成，然后立即隐藏到托盘
                m_window.Activate();
                HideMainWindow();
            }
            else
            {
                m_window.Activate();
            }
#else
            m_window.Activate();
#endif
        }

        /// <summary>
        /// 初始化系统托盘图标
        /// </summary>
        private void InitializeTrayIcon()
        {
            _trayIcon = new TrayIconHelper();
            _trayIcon.ShowWindowRequested += OnTrayShowWindow;
            _trayIcon.ExitRequested += OnTrayExit;
            _trayIcon.Initialize();
        }

        /// <summary>
        /// 窗口关闭事件处理 —— 最小化到托盘
        /// </summary>
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!_isExiting)
            {
                // 取消关闭，改为隐藏窗口到托盘
                args.Handled = true;
                HideMainWindow();

                // 首次最小化时显示气泡通知提示用户
                if (_firstMinimizeToTray)
                {
                    _firstMinimizeToTray = false;
                    _trayIcon?.ShowBalloonTip("GameSave Manager",
                        "程序已最小化到系统托盘，双击图标可重新打开。");
                }
            }
        }

        /// <summary>
        /// 隐藏主窗口
        /// </summary>
        private void HideMainWindow()
        {
            if (m_window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
                ShowWindow(hwnd, SW_HIDE);
            }
        }

        /// <summary>
        /// 显示并激活主窗口
        /// </summary>
        private void ShowMainWindow()
        {
            if (m_window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                m_window.Activate();
            }
        }

        /// <summary>
        /// 托盘图标 —— 显示主窗口
        /// </summary>
        private void OnTrayShowWindow()
        {
            ShowMainWindow();
        }

        /// <summary>
        /// 托盘图标 —— 退出应用
        /// </summary>
        private void OnTrayExit()
        {
            _isExiting = true;

            // 清理托盘图标
            _trayIcon?.Dispose();
            _trayIcon = null;

            // 关闭主窗口并退出应用
            m_window?.Close();
        }

        /// <summary>获取当前主窗口（用于弹出文件选择器等）</summary>
        public static Window? MainWindow => ((App)Current).m_window;

        /// <summary>
        /// 将主题模式字符串转换为 ElementTheme 枚举
        /// </summary>
        private static ElementTheme ParseThemeMode(string themeMode)
        {
            return themeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default  // "System" 或未知值 -> 跟随系统
            };
        }

        /// <summary>
        /// 获取当前实际使用的 ElementTheme（供 ContentDialog 等组件使用）
        /// </summary>
        public static ElementTheme GetCurrentTheme()
        {
            var window = ((App)Current).m_window;
            if (window?.Content is FrameworkElement rootElement)
            {
                // 如果 RequestedTheme 是 Default，则获取实际的系统主题
                if (rootElement.RequestedTheme == ElementTheme.Default)
                {
                    return rootElement.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark
                        ? ElementTheme.Dark
                        : ElementTheme.Light;
                }
                return rootElement.RequestedTheme;
            }
            return ElementTheme.Default;
        }

        /// <summary>
        /// 应用主题到当前窗口（包括内容区域和标题栏）
        /// </summary>
        public static void ApplyTheme(string themeMode)
        {
            var app = (App)Current;
            var window = app.m_window;
            if (window == null) return;

            // 1. 设置内容区域主题
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = ParseThemeMode(themeMode);
            }

            // 2. 设置标题栏颜色以匹配主题
            ApplyTitleBarTheme(window, themeMode);
        }

        /// <summary>
        /// 根据主题模式设置窗口标题栏颜色
        /// </summary>
        private static void ApplyTitleBarTheme(Window window, string themeMode)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var titleBar = appWindow.TitleBar;

            // 判断是否为深色主题
            bool isDark;
            if (themeMode == "Dark")
            {
                isDark = true;
            }
            else if (themeMode == "Light")
            {
                isDark = false;
            }
            else
            {
                // 跟随系统：检查当前窗口的实际主题
                isDark = window.Content is FrameworkElement fe
                    && fe.ActualTheme == ElementTheme.Dark;
            }

            if (isDark)
            {
                // 深色标题栏
                titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 153, 153, 153);
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 153, 153, 153);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 51, 51, 51);
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 64, 64, 64);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            }
            else
            {
                // 浅色标题栏（恢复默认值）
                titleBar.BackgroundColor = null;
                titleBar.ForegroundColor = null;
                titleBar.InactiveBackgroundColor = null;
                titleBar.InactiveForegroundColor = null;
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonInactiveBackgroundColor = null;
                titleBar.ButtonInactiveForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonPressedForegroundColor = null;
            }
        }

        /// <summary>
        /// 设置主题并保存配置（供设置页面调用）
        /// </summary>
        public static async Task SetThemeAsync(string themeMode)
        {
            ApplyTheme(themeMode);
            await ConfigService.SetThemeModeAsync(themeMode);
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("页面导航失败: " + e.SourcePageType.FullName);
        }
    }
}
