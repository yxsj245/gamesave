using GameSave.Models;
using GameSave.Services;

namespace GameSave.Views
{
    /// <summary>
    /// 设置页面
    /// </summary>
    public partial class SettingsPage : Page
    {
        /// <summary>标记是否正在加载设置（避免触发 Toggled 事件）</summary>
        private bool _isLoadingSettings;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        public ViewModels.SettingsViewModel ViewModel { get; } = new ViewModels.SettingsViewModel();

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _isLoadingSettings = true;
            ViewModel.LoadSettings();
            _isLoadingSettings = false;
            UpdateNoConfigHint();
        }

        /// <summary>
        /// 主题 RadioButtons 加载完成 — 设置选中索引
        /// </summary>
        private void ThemeRadioButtons_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.RadioButtons radioButtons)
            {
                _isLoadingSettings = true;
                radioButtons.SelectedIndex = ViewModel.SelectedThemeIndex;
                _isLoadingSettings = false;
            }
        }

        /// <summary>
        /// 主题 RadioButtons 选择变更
        /// </summary>
        private void ThemeRadioButtons_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (sender is Microsoft.UI.Xaml.Controls.RadioButtons radioButtons)
            {
                ViewModel.SelectedThemeIndex = radioButtons.SelectedIndex;
            }
        }

        /// <summary>
        /// 开机自启动开关加载完成 — 设置初始状态
        /// </summary>
        private void AutoStartToggle_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.ToggleSwitch toggle)
            {
                _isLoadingSettings = true;
                toggle.IsOn = ViewModel.IsAutoStartEnabled;
                _isLoadingSettings = false;
            }
        }

        /// <summary>
        /// 开机自启动开关切换事件
        /// </summary>
        private void AutoStartToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (sender is Microsoft.UI.Xaml.Controls.ToggleSwitch toggle)
            {
                ViewModel.IsAutoStartEnabled = toggle.IsOn;
            }
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

        /// <summary>
        /// 导出游戏 — 弹出批量选择对话框（支持全选）
        /// </summary>
        private async void ExportGames_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var allGames = ViewModel.GetAllGames();
            if (allGames.Count == 0)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "当前没有任何游戏可以导出",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await emptyDialog.ShowWithThemeAsync();
                return;
            }

            // 构建选择列表
            var formPanel = new StackPanel { Spacing = 8, MinWidth = 400 };

            // 全选复选框
            var selectAllCheckBox = new CheckBox
            {
                Content = "全选",
                IsChecked = true,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            };
            formPanel.Children.Add(selectAllCheckBox);

            // 分隔线
            var separator = new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            };
            formPanel.Children.Add(separator);

            // 游戏复选框列表
            var gameCheckBoxes = new List<(CheckBox checkBox, Game game)>();
            bool isUpdatingCheckBoxes = false;

            foreach (var game in allGames)
            {
                var cb = new CheckBox
                {
                    Content = game.Name,
                    IsChecked = true,
                    Tag = game
                };
                gameCheckBoxes.Add((cb, game));
                formPanel.Children.Add(cb);
            }

            // 全选联动逻辑
            selectAllCheckBox.Checked += (s, _) =>
            {
                if (isUpdatingCheckBoxes) return;
                isUpdatingCheckBoxes = true;
                foreach (var (cb, _) in gameCheckBoxes)
                    cb.IsChecked = true;
                isUpdatingCheckBoxes = false;
            };

            selectAllCheckBox.Unchecked += (s, _) =>
            {
                if (isUpdatingCheckBoxes) return;
                isUpdatingCheckBoxes = true;
                foreach (var (cb, _) in gameCheckBoxes)
                    cb.IsChecked = false;
                isUpdatingCheckBoxes = false;
            };

            // 子项联动全选
            foreach (var (cb, _) in gameCheckBoxes)
            {
                cb.Checked += (s, _) =>
                {
                    if (isUpdatingCheckBoxes) return;
                    isUpdatingCheckBoxes = true;
                    if (gameCheckBoxes.All(x => x.checkBox.IsChecked == true))
                        selectAllCheckBox.IsChecked = true;
                    else
                        selectAllCheckBox.IsChecked = null; // 不确定状态
                    isUpdatingCheckBoxes = false;
                };
                cb.Unchecked += (s, _) =>
                {
                    if (isUpdatingCheckBoxes) return;
                    isUpdatingCheckBoxes = true;
                    if (gameCheckBoxes.All(x => x.checkBox.IsChecked == false))
                        selectAllCheckBox.IsChecked = false;
                    else
                        selectAllCheckBox.IsChecked = null; // 不确定状态
                    isUpdatingCheckBoxes = false;
                };
            }

            var scrollViewer = new ScrollViewer
            {
                Content = formPanel,
                MaxHeight = 400
            };

            var selectDialog = new ContentDialog
            {
                Title = "选择要导出的游戏",
                PrimaryButtonText = "导出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = scrollViewer
            };

            var selectResult = await selectDialog.ShowWithThemeAsync();
            if (selectResult != ContentDialogResult.Primary)
                return;

            // 收集选中的游戏
            var selectedGames = gameCheckBoxes
                .Where(x => x.checkBox.IsChecked == true)
                .Select(x => x.game)
                .ToList();

            if (selectedGames.Count == 0)
            {
                var noSelectDialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "请至少选择一个游戏",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await noSelectDialog.ShowWithThemeAsync();
                return;
            }

            // 弹出保存文件对话框
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            savePicker.FileTypeChoices.Add("ZIP 压缩包", new List<string> { ".zip" });
            savePicker.SuggestedFileName = $"GameSave_Export_{DateTime.Now:yyyyMMdd_HHmmss}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null) return;

            // 显示进度条
            ProgressStatusText.Text = $"正在导出 {selectedGames.Count} 个游戏...";
            ExportImportProgressBar.Value = 0;
            ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

            var progress = new Progress<double>(value =>
            {
                ExportImportProgressBar.Value = value;
                ProgressStatusText.Text = $"正在导出 {selectedGames.Count} 个游戏... {value:F0}%";
            });

            // 执行导出（记录开始时间，确保进度条至少显示 500ms）
            var exportStartTime = DateTime.Now;
            var (success, message) = await ViewModel.ExportGamesAsync(selectedGames, file.Path, progress);

            var elapsed = (DateTime.Now - exportStartTime).TotalMilliseconds;
            if (elapsed < 500)
                await Task.Delay((int)(500 - elapsed));

            // 隐藏进度条
            ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            var resultDialog = new ContentDialog
            {
                Title = success ? "导出成功" : "导出失败",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await resultDialog.ShowWithThemeAsync();
        }

        /// <summary>
        /// 导入数据 — 选择 zip 文件并预览后导入
        /// </summary>
        private async void ImportGames_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // 打开文件选择器
            var openPicker = new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            openPicker.FileTypeFilter.Add(".zip");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);

            var file = await openPicker.PickSingleFileAsync();
            if (file == null) return;

            // 获取预览信息
            var (previewSuccess, previews, previewMessage) = await ViewModel.GetImportPreviewAsync(file.Path);
            if (!previewSuccess || previews == null)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "无法导入",
                    Content = previewMessage,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowWithThemeAsync();
                return;
            }

            // 构建预览内容
            var previewPanel = new StackPanel { Spacing = 8, MinWidth = 400 };
            previewPanel.Children.Add(new TextBlock
            {
                Text = $"发现 {previews.Count} 个游戏：",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            foreach (var preview in previews)
            {
                var itemPanel = new StackPanel { Spacing = 2, Margin = new Microsoft.UI.Xaml.Thickness(8, 4, 0, 4) };

                var nameText = new TextBlock
                {
                    Text = preview.Name,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };

                var detailText = new TextBlock
                {
                    Text = $"存档数: {preview.SaveCount}　路径: {preview.SaveFolderPath}",
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords
                };

                itemPanel.Children.Add(nameText);
                itemPanel.Children.Add(detailText);

                if (preview.AlreadyExists)
                {
                    var existsText = new TextBlock
                    {
                        Text = "⚠️ 本地已存在同名游戏，将跳过导入",
                        FontSize = 12,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                    };
                    itemPanel.Children.Add(existsText);
                }

                previewPanel.Children.Add(itemPanel);
            }

            var previewScrollViewer = new ScrollViewer
            {
                Content = previewPanel,
                MaxHeight = 400
            };

            var confirmDialog = new ContentDialog
            {
                Title = "确认导入",
                PrimaryButtonText = "开始导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = previewScrollViewer
            };

            var confirmResult = await confirmDialog.ShowWithThemeAsync();
            if (confirmResult != ContentDialogResult.Primary)
                return;

            // 显示进度条
            ProgressStatusText.Text = "正在导入数据...";
            ExportImportProgressBar.Value = 0;
            ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

            var importProgress = new Progress<double>(value =>
            {
                ExportImportProgressBar.Value = value;
                ProgressStatusText.Text = $"正在导入数据... {value:F0}%";
            });

            // 执行导入（记录开始时间，确保进度条至少显示 500ms）
            var importStartTime = DateTime.Now;
            var (importSuccess, importMessage) = await ViewModel.ImportGamesAsync(file.Path, importProgress);

            var importElapsed = (DateTime.Now - importStartTime).TotalMilliseconds;
            if (importElapsed < 500)
                await Task.Delay((int)(500 - importElapsed));

            // 隐藏进度条
            ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            var resultDialog = new ContentDialog
            {
                Title = importSuccess ? "导入完成" : "导入失败",
                Content = importMessage,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await resultDialog.ShowWithThemeAsync();
        }
    }
}

