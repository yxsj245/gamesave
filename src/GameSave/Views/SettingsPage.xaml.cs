using GameSave.Models;
using GameSave.Services;
using System.Runtime.InteropServices;

namespace GameSave.Views
{
    /// <summary>
    /// 设置页面
    /// </summary>
    public partial class SettingsPage : Page
    {
        /// <summary>标记是否正在加载设置（避免触发 Toggled 事件）</summary>
        private bool _isLoadingSettings;
        private bool _isRecordingQuickSaveHotkey;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

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
            UpdateQuickSaveHotkeyStatus();

            // 检查工作目录是否被启动参数锁定
            if (ViewModel.IsWorkDirLocked)
            {
                ChangeWorkDirButton.IsEnabled = false;
                WorkDirLockHint.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }
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
        /// 快捷存档开关加载完成。
        /// </summary>
        private void QuickSaveHotkeyToggle_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle) return;

            _isLoadingSettings = true;
            toggle.IsOn = ViewModel.IsQuickSaveHotkeyEnabled;
            _isLoadingSettings = false;
        }

        /// <summary>
        /// 启用或关闭快捷存档快捷键。
        /// </summary>
        private async void QuickSaveHotkeyToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not ToggleSwitch toggle) return;

            await ViewModel.ApplyQuickSaveHotkeyAsync(toggle.IsOn, ViewModel.QuickSaveHotkey);
            if (toggle.IsOn != ViewModel.IsQuickSaveHotkeyEnabled)
            {
                _isLoadingSettings = true;
                toggle.IsOn = ViewModel.IsQuickSaveHotkeyEnabled;
                _isLoadingSettings = false;
            }

            UpdateQuickSaveHotkeyStatus();
        }

        /// <summary>
        /// 进入快捷键录制状态。
        /// </summary>
        private void RecordQuickSaveHotkey_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _isRecordingQuickSaveHotkey = true;
            QuickSaveHotkeyTextBox.Text = "请按下新的快捷键组合…";
            QuickSaveHotkeyTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            QuickSaveHotkeyTextBox.SelectAll();
            QuickSaveHotkeyStatusText.Text = "请按住 Ctrl、Alt 或 Shift，再按 A-Z、0-9 或 F1-F11";
            SetQuickSaveHotkeyStatusBrush(isError: false);
        }

        /// <summary>
        /// 恢复默认快捷键 Ctrl+Shift+F5。
        /// </summary>
        private async void ResetQuickSaveHotkey_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _isRecordingQuickSaveHotkey = false;
            await ViewModel.ApplyQuickSaveHotkeyAsync(
                ViewModel.IsQuickSaveHotkeyEnabled,
                HotkeyGesture.Default);
            QuickSaveHotkeyTextBox.Text = ViewModel.QuickSaveHotkeyDisplayText;
            UpdateQuickSaveHotkeyStatus();
        }

        /// <summary>
        /// 捕获用户输入的快捷键组合。
        /// </summary>
        private async void QuickSaveHotkeyTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (!_isRecordingQuickSaveHotkey) return;

            e.Handled = true;
            var virtualKey = (int)e.Key;
            if (virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
                return;

            var modifiers = HotkeyModifiers.None;
            if (IsKeyDown(0x11)) modifiers |= HotkeyModifiers.Control;
            if (IsKeyDown(0x12)) modifiers |= HotkeyModifiers.Alt;
            if (IsKeyDown(0x10)) modifiers |= HotkeyModifiers.Shift;
            if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) modifiers |= HotkeyModifiers.Windows;

            var gesture = new HotkeyGesture(modifiers, virtualKey);
            if (!gesture.IsValid(out var validationMessage))
            {
                QuickSaveHotkeyStatusText.Text = validationMessage;
                SetQuickSaveHotkeyStatusBrush(isError: true);
                return;
            }

            var (success, _) = await ViewModel.ApplyQuickSaveHotkeyAsync(
                ViewModel.IsQuickSaveHotkeyEnabled,
                gesture);
            if (success)
                _isRecordingQuickSaveHotkey = false;

            QuickSaveHotkeyTextBox.Text = success
                ? ViewModel.QuickSaveHotkeyDisplayText
                : "请按下新的快捷键组合…";
            UpdateQuickSaveHotkeyStatus();
        }

        private static bool IsKeyDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

        private void UpdateQuickSaveHotkeyStatus()
        {
            QuickSaveHotkeyStatusText.Text = ViewModel.QuickSaveHotkeyStatusMessage;
            SetQuickSaveHotkeyStatusBrush(ViewModel.IsQuickSaveHotkeyStatusError);
        }

        private void SetQuickSaveHotkeyStatusBrush(bool isError)
        {
            var resourceKey = isError
                ? "SystemFillColorCriticalBrush"
                : "TextFillColorSecondaryBrush";
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource)
                && resource is Microsoft.UI.Xaml.Media.Brush brush)
            {
                QuickSaveHotkeyStatusText.Foreground = brush;
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

        private const string PasswordRevealIconFontFamily = "Segoe MDL2 Assets";
        private const string PasswordRevealGlyph = "\uE890";

        private static string GetPasswordRevealGlyph(bool isPasswordVisible) =>
            PasswordRevealGlyph;

        private static string GetPasswordRevealIconFontFamily() => PasswordRevealIconFontFamily;

        private static double GetPasswordRevealIconSize() => 12d;

        private static double GetPasswordRevealButtonWidth() => 24d;

        private static double GetPasswordRevealButtonHeight() => 24d;

        private static Microsoft.UI.Xaml.Thickness GetPasswordRevealInputPadding() =>
            new(12, 5, 34, 5);

        private static double GetPasswordRevealIconOpacity(bool isPasswordVisible) =>
            isPasswordVisible ? 1d : 0.68d;

        private static string GetPasswordRevealButtonBackgroundResourceKey() =>
            "TextControlBackgroundFocused";

        private static Microsoft.UI.Xaml.Media.Brush GetPasswordRevealButtonBackground()
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
                    GetPasswordRevealButtonBackgroundResourceKey(),
                    out var resource)
                && resource is Microsoft.UI.Xaml.Media.Brush brush)
            {
                return brush;
            }

            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private static Microsoft.UI.Xaml.Media.Brush GetPasswordRevealIconForeground(bool isPasswordVisible)
        {
            var resourceKey = isPasswordVisible
                ? "AccentFillColorDefaultBrush"
                : "TextFillColorSecondaryBrush";

            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource)
                && resource is Microsoft.UI.Xaml.Media.Brush brush)
            {
                return brush;
            }

            return new Microsoft.UI.Xaml.Media.SolidColorBrush(
                isPasswordVisible ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.Gray);
        }

        private static void UpdatePasswordRevealIconVisual(FontIcon revealIcon, bool isPasswordVisible)
        {
            revealIcon.Glyph = GetPasswordRevealGlyph(isPasswordVisible);
            revealIcon.Opacity = GetPasswordRevealIconOpacity(isPasswordVisible);
            revealIcon.Foreground = GetPasswordRevealIconForeground(isPasswordVisible);
        }

        private static FontIcon CreatePasswordRevealIcon()
        {
            var revealIcon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(GetPasswordRevealIconFontFamily()),
                FontSize = GetPasswordRevealIconSize(),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            UpdatePasswordRevealIconVisual(revealIcon, false);
            return revealIcon;
        }

        private static (PasswordBox passwordBox, FrameworkElement control) CreateRevealablePasswordInput(
            string header,
            string placeholder,
            string initialPassword)
        {
            var container = new StackPanel { Spacing = 4 };
            container.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 14
            });

            var inputGrid = new Grid();

            var passwordBox = new PasswordBox
            {
                PlaceholderText = placeholder,
                Password = initialPassword,
                PasswordRevealMode = PasswordRevealMode.Hidden,
                Padding = GetPasswordRevealInputPadding()
            };

            var visibleBox = new TextBox
            {
                PlaceholderText = placeholder,
                Text = initialPassword,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed,
                Padding = GetPasswordRevealInputPadding()
            };

            var revealIcon = CreatePasswordRevealIcon();
            var revealButtonBackground = GetPasswordRevealButtonBackground();

            var revealButton = new Button
            {
                Width = GetPasswordRevealButtonWidth(),
                MinWidth = GetPasswordRevealButtonWidth(),
                Height = GetPasswordRevealButtonHeight(),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 4, 0),
                Padding = new Microsoft.UI.Xaml.Thickness(0),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                UseSystemFocusVisuals = false,
                Background = revealButtonBackground,
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Content = revealIcon
            };
            revealButton.Resources["ButtonBackground"] = revealButtonBackground;
            revealButton.Resources["ButtonBackgroundPointerOver"] = revealButtonBackground;
            revealButton.Resources["ButtonBackgroundPressed"] = revealButtonBackground;
            revealButton.Resources["ButtonBackgroundDisabled"] = revealButtonBackground;
            revealButton.Resources["ButtonBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            revealButton.Resources["ButtonBorderBrushPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            revealButton.Resources["ButtonBorderBrushPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ToolTipService.SetToolTip(revealButton, "显示密码");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(revealButton, "显示密码");

            inputGrid.Children.Add(passwordBox);
            inputGrid.Children.Add(visibleBox);
            inputGrid.Children.Add(revealButton);

            var syncing = false;
            var isPasswordVisible = false;
            passwordBox.PasswordChanged += (_, _) =>
            {
                if (syncing) return;
                syncing = true;
                visibleBox.Text = passwordBox.Password;
                syncing = false;
            };

            visibleBox.TextChanged += (_, _) =>
            {
                if (syncing) return;
                syncing = true;
                passwordBox.Password = visibleBox.Text;
                syncing = false;
            };

            revealButton.Click += (_, _) =>
            {
                isPasswordVisible = !isPasswordVisible;
                UpdatePasswordRevealIconVisual(revealIcon, isPasswordVisible);
                if (isPasswordVisible)
                {
                    visibleBox.Text = passwordBox.Password;
                    passwordBox.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    visibleBox.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(revealButton, "隐藏密码");
                    ToolTipService.SetToolTip(revealButton, "隐藏密码");
                    visibleBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                }
                else
                {
                    passwordBox.Password = visibleBox.Text;
                    visibleBox.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    passwordBox.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(revealButton, "显示密码");
                    ToolTipService.SetToolTip(revealButton, "显示密码");
                    passwordBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                }
            };

            container.Children.Add(inputGrid);
            return (passwordBox, container);
        }

        private bool _isChangingWorkDirectory;

        private async void ChangeWorkDir_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.IsWorkDirLocked || _isChangingWorkDirectory) return;
            _isChangingWorkDirectory = true;
            ChangeWorkDirButton.IsEnabled = false;
            try
            {
                var selectedPath = await ShellDialogHelper.PickFolderAsync(App.MainWindow);
                if (string.IsNullOrWhiteSpace(selectedPath)) return;
                var newPath = Helpers.WorkDirectorySafety.NormalizeDestination(selectedPath);
                var oldPath = Helpers.WorkDirectorySafety.NormalizePath(ViewModel.WorkDirectory);
                if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    await ShowWorkDirectoryMessageAsync("提示", "选择的目录与当前工作目录相同，无需更改。");
                    return;
                }

                var migrateDialog = new ContentDialog
                {
                    Title = "切换工作目录",
                    Content = $"旧目录: {oldPath}\n新目录: {newPath}\n\n「复制并切换」只复制已登记游戏的数据，旧目录的全部内容保留，不覆盖目标中的已有游戏数据。\n「仅切换」不复制任何数据，旧目录内容也保留。\n\n请先退出游戏并停止备份。复制期间不要启动游戏或进行其他备份操作。",
                    PrimaryButtonText = "复制并切换",
                    SecondaryButtonText = "仅切换",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                var result = await migrateDialog.ShowWithThemeAsync();
                if (result == ContentDialogResult.None) return;
                if (result == ContentDialogResult.Secondary)
                {
                    await ViewModel.ChangeWorkDirectoryAsync(newPath);
                    await ShowWorkDirectoryMessageAsync("更改完成", $"工作目录已更改为:\n{ViewModel.WorkDirectory}\n\n旧目录内容全部保留。");
                    return;
                }

                ProgressStatusText.Text = "正在复制并校验游戏数据...";
                ExportImportProgressBar.Value = 0;
                ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                var progress = new Progress<(double percent, string status)>(report =>
                {
                    ExportImportProgressBar.Value = report.percent;
                    ProgressStatusText.Text = report.status;
                });
                await ViewModel.ChangeWorkDirectoryWithMigrationAsync(newPath, progress);
                await ShowWorkDirectoryMessageAsync("复制并切换完成",
                    $"工作目录已切换到:\n{ViewModel.WorkDirectory}\n\n旧目录的全部内容仍然保留，不会自动删除。配置文件仍保存在原来的位置。");
            }
            catch (Exception ex)
            {
                await ShowWorkDirectoryMessageAsync("更改未完成",
                    $"操作未完成：\n{ex.Message}\n\n旧目录不会被本操作删除。若复制中断，新目录可能保留部分副本，请勿直接删除不明文件或反复迁移。");
            }
            finally
            {
                ProgressPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                _isChangingWorkDirectory = false;
                ChangeWorkDirButton.IsEnabled = !ViewModel.IsWorkDirLocked;
            }
        }

        private async Task ShowWorkDirectoryMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowWithThemeAsync();
        }

        /// <summary>
        /// 添加云端配置 — 弹出 ContentDialog
        /// </summary>
        private async void AddCloudConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.ResetCloudConfigForm();
            await ShowCloudConfigDialogAsync();
        }

        private async Task ShowCloudConfigDialogAsync()
        {
            // 构建表单内容
            var formPanel = new StackPanel { Spacing = 12, MinWidth = 400 };

            var nameBox = new TextBox
            {
                Header = "配置名称",
                PlaceholderText = "例如: 我的阿里云OSS",
                Text = ViewModel.NewDisplayName
            };

            var providerCombo = new ComboBox
            {
                Header = "服务商",
                SelectedIndex = ViewModel.NewProviderType == CloudProviderType.WebDav ? 1 : 0,
                MinWidth = 220
            };
            providerCombo.Items.Add("阿里云 OSS");
            providerCombo.Items.Add("WebDAV");

            var endpointBox = new TextBox
            {
                Header = "Endpoint",
                PlaceholderText = "例如: oss-cn-hangzhou.aliyuncs.com",
                Text = ViewModel.NewEndpoint
            };

            var bucketBox = new TextBox
            {
                Header = "Bucket 名称",
                PlaceholderText = "存储桶名称",
                Text = ViewModel.NewBucketName
            };

            var akIdBox = new TextBox
            {
                Header = "AccessKey ID",
                PlaceholderText = "阿里云 AccessKey ID",
                Text = ViewModel.NewAccessKeyId
            };

            var (akSecretBox, akSecretInput) = CreateRevealablePasswordInput(
                "AccessKey Secret",
                "阿里云 AccessKey Secret",
                ViewModel.NewAccessKeySecret);

            var basePathBox = new TextBox
            {
                Header = "远端存储路径",
                PlaceholderText = "默认: GameSave",
                Text = ViewModel.NewRemoteBasePath
            };

            var serverUrlBox = new TextBox
            {
                Header = "WebDAV 服务器地址",
                PlaceholderText = "例如: https://192.168.1.99:5006",
                Text = ViewModel.NewServerUrl
            };

            var usernameBox = new TextBox
            {
                Header = "用户名",
                PlaceholderText = "WebDAV 用户名",
                Text = ViewModel.NewUsername
            };

            var (passwordBox, passwordInput) = CreateRevealablePasswordInput(
                "密码",
                "WebDAV 密码",
                ViewModel.NewPassword);

            var ossFields = new StackPanel { Spacing = 12 };
            ossFields.Children.Add(endpointBox);
            ossFields.Children.Add(bucketBox);
            ossFields.Children.Add(akIdBox);
            ossFields.Children.Add(akSecretInput);

            var webDavFields = new StackPanel
            {
                Spacing = 12,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };
            webDavFields.Children.Add(serverUrlBox);
            webDavFields.Children.Add(usernameBox);
            webDavFields.Children.Add(passwordInput);

            void ApplyProviderSelection()
            {
                bool isWebDav = providerCombo.SelectedIndex == 1;
                ossFields.Visibility = isWebDav
                    ? Microsoft.UI.Xaml.Visibility.Collapsed
                    : Microsoft.UI.Xaml.Visibility.Visible;
                webDavFields.Visibility = isWebDav
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
                basePathBox.PlaceholderText = "默认: GameSave";

                if (string.IsNullOrWhiteSpace(basePathBox.Text))
                    basePathBox.Text = "GameSave";
            }

            providerCombo.SelectionChanged += (_, _) => ApplyProviderSelection();

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
                ViewModel.NewProviderType = providerCombo.SelectedIndex == 1
                    ? CloudProviderType.WebDav
                    : CloudProviderType.AliyunOss;
                ViewModel.NewDisplayName = nameBox.Text;
                ViewModel.NewEndpoint = endpointBox.Text;
                ViewModel.NewBucketName = bucketBox.Text;
                ViewModel.NewAccessKeyId = akIdBox.Text;
                ViewModel.NewAccessKeySecret = akSecretBox.Password;
                ViewModel.NewServerUrl = serverUrlBox.Text;
                ViewModel.NewUsername = usernameBox.Text;
                ViewModel.NewPassword = passwordBox.Password;
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
            formPanel.Children.Add(providerCombo);
            formPanel.Children.Add(ossFields);
            formPanel.Children.Add(webDavFields);
            formPanel.Children.Add(basePathBox);
            formPanel.Children.Add(testBtn);
            formPanel.Children.Add(testResultText);
            ApplyProviderSelection();

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
                    Title = ViewModel.IsEditingCloudConfig ? "编辑云端存储配置" : "添加云端存储配置",
                    PrimaryButtonText = ViewModel.IsEditingCloudConfig ? "保存" : "添加",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Content = scrollViewer
                };

                var result = await dialog.ShowWithThemeAsync();

                if (result != ContentDialogResult.Primary)
                    break; // 用户取消

                // 同步表单数据到 ViewModel
                ViewModel.NewProviderType = providerCombo.SelectedIndex == 1
                    ? CloudProviderType.WebDav
                    : CloudProviderType.AliyunOss;
                ViewModel.NewDisplayName = nameBox.Text;
                ViewModel.NewEndpoint = endpointBox.Text;
                ViewModel.NewBucketName = bucketBox.Text;
                ViewModel.NewAccessKeyId = akIdBox.Text;
                ViewModel.NewAccessKeySecret = akSecretBox.Password;
                ViewModel.NewServerUrl = serverUrlBox.Text;
                ViewModel.NewUsername = usernameBox.Text;
                ViewModel.NewPassword = passwordBox.Password;
                ViewModel.NewRemoteBasePath = basePathBox.Text;

                var (success, message) = await ViewModel.SaveCloudConfigAsync();
                if (success)
                {
                    UpdateNoConfigHint();
                    break; // 保存成功，退出循环
                }

                // 验证失败，显示错误信息并重新弹出
                testResultText.Text = $"❌ {message}";
                testResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
            }
        }

        /// <summary>
        /// 编辑云端配置
        /// </summary>
        private async void EditCloudConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CloudConfig config)
            {
                ViewModel.BeginEditCloudConfig(config);
                await ShowCloudConfigDialogAsync();
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
            var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".zip"]);
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // 获取预览信息
            var (previewSuccess, previews, previewMessage) = await ViewModel.GetImportPreviewAsync(filePath);
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
            var (importSuccess, importMessage) = await ViewModel.ImportGamesAsync(filePath, importProgress);

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

