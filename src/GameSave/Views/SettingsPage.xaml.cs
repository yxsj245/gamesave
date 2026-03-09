namespace GameSave.Views
{
    /// <summary>
    /// 设置页面
    /// </summary>
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        public ViewModels.SettingsViewModel ViewModel { get; } = new ViewModels.SettingsViewModel();

        private async void ChangeWorkDir_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.ChangeWorkDirectoryAsync(folder.Path);
            }
        }
    }
}
