using GameSave.Models;

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

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.LoadSettings();
            UpdateNoConfigHint();
        }

        /// <summary>
        /// 更新空列表提示的可见性
        /// </summary>
        private void UpdateNoConfigHint()
        {
            NoConfigHint.Visibility = ViewModel.CloudConfigs.Count == 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

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

        /// <summary>
        /// 添加云端配置 — 弹出 ContentDialog
        /// </summary>
        private async void AddCloudConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.ResetCloudConfigForm();

            // 构建表单内容
            var formPanel = new StackPanel { Spacing = 12, MinWidth = 400 };

            var nameBox = new TextBox
            {
                Header = "配置名称",
                PlaceholderText = "例如: 我的阿里云OSS"
            };

            var providerInfo = new TextBlock
            {
                Text = "服务商: 阿里云 OSS",
                FontSize = 13,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0)
            };

            var endpointBox = new TextBox
            {
                Header = "Endpoint",
                PlaceholderText = "例如: oss-cn-hangzhou.aliyuncs.com"
            };

            var bucketBox = new TextBox
            {
                Header = "Bucket 名称",
                PlaceholderText = "存储桶名称"
            };

            var akIdBox = new TextBox
            {
                Header = "AccessKey ID",
                PlaceholderText = "阿里云 AccessKey ID"
            };

            var akSecretBox = new PasswordBox
            {
                Header = "AccessKey Secret",
                PlaceholderText = "阿里云 AccessKey Secret"
            };

            var basePathBox = new TextBox
            {
                Header = "远端存储路径",
                PlaceholderText = "默认: GameSave",
                Text = "GameSave"
            };

            var testResultText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0)
            };

            // 测试连接按钮（放在表单内，避免 SecondaryButton 的 async 问题）
            var testBtn = new Button
            {
                Content = "测试连接",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
            };
            testBtn.Click += async (s, _) =>
            {
                // 同步更新 ViewModel 数据
                ViewModel.NewDisplayName = nameBox.Text;
                ViewModel.NewEndpoint = endpointBox.Text;
                ViewModel.NewBucketName = bucketBox.Text;
                ViewModel.NewAccessKeyId = akIdBox.Text;
                ViewModel.NewAccessKeySecret = akSecretBox.Password;
                ViewModel.NewRemoteBasePath = basePathBox.Text;

                testResultText.Text = "⏳ 正在测试连接...";
                testResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                testBtn.IsEnabled = false;

                await ViewModel.TestCloudConnectionAsync();

                testResultText.Text = ViewModel.TestResult;
                testResultText.Foreground = ViewModel.TestResult.Contains("✅")
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                testBtn.IsEnabled = true;
            };

            formPanel.Children.Add(nameBox);
            formPanel.Children.Add(providerInfo);
            formPanel.Children.Add(endpointBox);
            formPanel.Children.Add(bucketBox);
            formPanel.Children.Add(akIdBox);
            formPanel.Children.Add(akSecretBox);
            formPanel.Children.Add(basePathBox);
            formPanel.Children.Add(testBtn);
            formPanel.Children.Add(testResultText);

            var scrollViewer = new ScrollViewer
            {
                Content = formPanel,
                MaxHeight = 500
            };

            // 使用循环模式：验证失败则重新弹出对话框
            while (true)
            {
                var dialog = new ContentDialog
                {
                    Title = "添加云端存储配置",
                    PrimaryButtonText = "添加",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Content = scrollViewer
                };

                var result = await dialog.ShowWithThemeAsync();

                if (result != ContentDialogResult.Primary)
                    break; // 用户取消

                // 同步表单数据到 ViewModel
                ViewModel.NewDisplayName = nameBox.Text;
                ViewModel.NewEndpoint = endpointBox.Text;
                ViewModel.NewBucketName = bucketBox.Text;
                ViewModel.NewAccessKeyId = akIdBox.Text;
                ViewModel.NewAccessKeySecret = akSecretBox.Password;
                ViewModel.NewRemoteBasePath = basePathBox.Text;

                var (success, message) = await ViewModel.AddCloudConfigAsync();
                if (success)
                {
                    UpdateNoConfigHint();
                    break; // 添加成功，退出循环
                }

                // 验证失败，显示错误信息并重新弹出
                testResultText.Text = $"❌ {message}";
                testResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
            }
        }

        /// <summary>
        /// 删除云端配置
        /// </summary>
        private async void DeleteCloudConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CloudConfig config)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除云端配置 \"{config.DisplayName}\" 吗？\n已关联此配置的游戏将不再自动同步。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await confirmDialog.ShowWithThemeAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await ViewModel.RemoveCloudConfigAsync(config);
                    UpdateNoConfigHint();
                }
            }
        }
    }
}
