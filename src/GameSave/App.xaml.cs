using GameSave.Services;
using Microsoft.UI.Xaml.Navigation;

namespace GameSave
{
    /// <summary>
    /// 应用程序入口
    /// </summary>
    public partial class App : Application
    {
        private Window? m_window;

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
                System.IO.File.WriteAllText("crash.log", e.ExceptionObject.ToString());
            };

            Microsoft.UI.Xaml.Application.Current.UnhandledException += (s, e) =>
            {
                System.IO.File.WriteAllText("xaml_crash.log", e.Exception.ToString());
                e.Handled = true;
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            m_window = new Window();
            m_window.Title = "GameSave Manager";

            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            m_window.Content = rootFrame;
            rootFrame.Navigate(typeof(Views.MainPage), e.Arguments);
            m_window.Activate();
        }

        /// <summary>获取当前主窗口（用于弹出文件选择器等）</summary>
        public static Window? MainWindow => ((App)Current).m_window;

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("页面导航失败: " + e.SourcePageType.FullName);
        }
    }
}
