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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

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

        /// <summary>
        /// 标记当前是否有通过应用启动的游戏正在运行（用于判断是否发送托盘通知）
        /// </summary>
        private bool _gameLaunchedViaApp = false;

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

            // 如果通过 --workdir 参数指定了工作目录，检查该目录是否存在
            if (ConfigService.IsWorkDirLockedByArgs && !Directory.Exists(ConfigService.WorkDirectory))
            {
                // 使用 Win32 MessageBox（此时 WinUI 窗口尚未初始化，无法使用 ContentDialog）
                MessageBoxW(
                    IntPtr.Zero,
                    $"启动参数 --workdir 指定的工作目录不存在：\n\n{ConfigService.WorkDirectory}\n\n请检查路径是否正确后重试。",
                    "GameSave Manager - 工作目录错误",
                    0x00000010 /* MB_ICONERROR */);
                Environment.Exit(1);
                return;
            }

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

            // 订阅游戏状态变更事件，用于在游戏退出后通过系统托盘通知用户备份结果
            SubscribeGameStatusForTrayNotification();

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

        /// <summary>
        /// 订阅游戏状态变更事件，在游戏退出后通过系统托盘通知用户备份结果
        /// 不再恢复窗口，改为全程后台运行 + 托盘通知
        /// </summary>
        private void SubscribeGameStatusForTrayNotification()
        {
            GameService.StatusChanged += (_, e) =>
            {
                // 只处理通过应用启动的游戏的状态变更
                if (!_gameLaunchedViaApp) return;

                switch (e.Status)
                {
                    case Services.GameRunStatus.Completed:
                        // 备份完成，发送成功通知
                        _trayIcon?.ShowBalloonTip("✅ 存档备份成功",
                            $"「{e.GameName}」的存档已备份完成。");
                        break;

                    case Services.GameRunStatus.Idle:
                        // 恢复空闲状态，重置标记
                        _gameLaunchedViaApp = false;
                        break;
                }
            };

            // 订阅崩溃检测事件
            GameService.GameCrashDetected += (_, gameName) =>
            {
                if (!_gameLaunchedViaApp) return;

                _trayIcon?.ShowBalloonTip("⚠️ 游戏异常退出",
                    $"检测到「{gameName}」异常退出或未成功启动，本次不备份存档。");
                _gameLaunchedViaApp = false;
            };

            // 订阅备份失败事件
            GameService.BackupFailed += (_, args) =>
            {
                if (!_gameLaunchedViaApp) return;

                _trayIcon?.ShowBalloonTip("❌ 存档备份失败",
                    $"「{args.GameName}」的存档备份失败：{args.ErrorMessage}");
                _gameLaunchedViaApp = false;
            };
        }

        /// <summary>
        /// 启动游戏后隐藏窗口到系统托盘并发送通知
        /// 供 HomePage 等页面在启动游戏成功后调用
        /// DEBUG 模式下无托盘图标，回退为最小化窗口
        /// </summary>
        /// <param name="gameName">启动的游戏名称</param>
        public static void HideToTrayForGame(string gameName)
        {
            var app = (App)Current;
            app._gameLaunchedViaApp = true;

            if (app._trayIcon != null)
            {
                // 发布模式：隐藏到系统托盘并发送通知
                app.HideMainWindow();
                app._trayIcon.ShowBalloonTip("🎮 游戏已启动",
                    $"「{gameName}」已启动，程序已进入后台运行。\n游戏退出后将自动备份存档并通知您。");
            }
            else
            {
                // 开发模式：无托盘图标，最小化窗口（避免隐藏后无法恢复）
                Views.MainPage.MinimizeWindow();
            }
        }

        /// <summary>
        /// 发送系统托盘通知（公共方法，供其他模块调用）
        /// </summary>
        public static void ShowTrayNotification(string title, string message)
        {
            var app = (App)Current;
            app._trayIcon?.ShowBalloonTip(title, message);
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
