using Microsoft.UI.Xaml.Navigation;

namespace GameSave
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? m_window;

        public App()
        {
            this.InitializeComponent();

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

            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            // 将根内容设定为包含 MainPage 的帧
            m_window.Content = rootFrame;

            rootFrame.Navigate(typeof(Views.MainPage), e.Arguments);
            m_window.Activate();
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
