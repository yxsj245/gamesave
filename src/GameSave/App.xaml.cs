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

        public App()
        {
            this.InitializeComponent();

            // 初始化服务依赖链
            LocalStorageService = new LocalStorageService(ConfigService);
            GameService = new GameService(ConfigService, LocalStorageService, ProcessMonitorService);

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

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
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
            m_window.Activate();

#if !DEBUG
            // 仅在发布模式下启用系统托盘（开发环境下关闭窗口直接退出，方便调试）
            InitializeTrayIcon();
            m_window.Closed += MainWindow_Closed;
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

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("页面导航失败: " + e.SourcePageType.FullName);
        }
    }
}
