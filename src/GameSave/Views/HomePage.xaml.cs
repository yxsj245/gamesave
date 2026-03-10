using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;

namespace GameSave.Views
{
    /// <summary>
    /// 主页 - 游戏列表和存档管理
    /// </summary>
    public partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 监听游戏崩溃检测事件，弹窗提示用户
            App.GameService.GameCrashDetected += async (_, gameName) =>
            {
                var dispatcherQueue = App.MainWindow?.DispatcherQueue;
                if (dispatcherQueue != null)
                {
                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            await ShowMessageAsync("⚠️ 异常退出",
                                $"检测到「{gameName}」程序崩溃或未成功启动，本次退出存档将不再备份。\n\n" +
                                "可能的原因：\n" +
                                "• Steam 等启动器劫持了进程，但未成功启动游戏\n" +
                                "• 游戏进程异常终止");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[HomePage] 崩溃提示弹窗异常: {ex.Message}");
                        }
                    });
                }
            };
        }

        public MainViewModel ViewModel { get; } = new MainViewModel();

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomePage] 初始化异常: {ex}");
            }
            UpdateEmptyState();
        }

        /// <summary>更新空状态提示的可见性</summary>
        private void UpdateEmptyState()
        {
            EmptyStatePanel.Visibility = ViewModel.Games.Count == 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        #region 详情浮层动画

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.IsDetailsVisible))
            {
                if (ViewModel.IsDetailsVisible)
                {
                    DetailsOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    ShowDetailsStoryboard.Begin();
                }
                else
                {
                    HideDetailsStoryboard.Begin();
                }
            }
        }

        private void HideDetailsStoryboard_Completed(object sender, object e)
        {
            if (!ViewModel.IsDetailsVisible)
            {
                DetailsOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        }

        #endregion

        #region 游戏列表项交互

        private void GameListItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var glowRect = element.FindName("GlowRect") as Microsoft.UI.Xaml.Shapes.Rectangle;
                if (glowRect != null)
                {
                    glowRect.Opacity = 1;
                }
            }
        }

        private void GameListItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var glowRect = element.FindName("GlowRect") as Microsoft.UI.Xaml.Shapes.Rectangle;
                if (glowRect != null)
                {
                    glowRect.Opacity = 0;
                }
            }
        }

        private void GameListItem_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var glowRect = element.FindName("GlowRect") as Microsoft.UI.Xaml.Shapes.Rectangle;
                var glowBrush = element.FindName("GlowBrush") as Microsoft.UI.Xaml.Media.RadialGradientBrush;

                if (glowRect != null && glowBrush != null)
                {
                    var pointerPosition = e.GetCurrentPoint(element).Position;
                    // Calculate relative position based on item size
                    double xRelative = pointerPosition.X / element.ActualWidth;
                    double yRelative = pointerPosition.Y / element.ActualHeight;

                    glowBrush.Center = new Windows.Foundation.Point(xRelative, yRelative);
                    glowBrush.GradientOrigin = new Windows.Foundation.Point(xRelative, yRelative);
                }
            }
        }

        private void GamesList_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // 检查点击源是否来自按钮控件（启动/停止按钮），如果是则不打开详情页
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !ReferenceEquals(source, sender))
            {
                if (source is Button)
                {
                    // 点击来自按钮，不打开详情面板
                    return;
                }
                source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
            }

            if (e.OriginalSource is FrameworkElement originalElement && originalElement.DataContext is Game game)
            {
                ViewModel.SelectedGame = game;
            }
        }

        private async void ListItemLaunchGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Game game)
            {
                if (game.IsRunning)
                {
                    // 停止游戏 - 异步执行并显示加载反馈
                    await ViewModel.StopGameDirectAsync(game);
                }
                else
                {
                    // 启动游戏
                    if (string.IsNullOrWhiteSpace(game.ProcessPath))
                    {
                        await ShowMessageAsync("无法启动", "该游戏未设置启动进程路径，请编辑游戏信息后再试。");
                        return;
                    }

                    var (success, message) = await ViewModel.LaunchGameDirectAsync(game);
                    if (!success)
                    {
                        await ShowMessageAsync("启动失败", message);
                    }
                }
            }
        }

        private void CloseDetails_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.CloseDetails();
        }

        /// <summary>列表项 - 启动定时备份</summary>
        private void ListItemStartScheduledBackup_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Game game)
            {
                ViewModel.StartScheduledBackupManual(game);
            }
        }

        /// <summary>列表项 - 停止定时备份</summary>
        private void ListItemStopScheduledBackup_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Game game)
            {
                ViewModel.StopScheduledBackupManual(game);
            }
        }

        #endregion

        #region 添加游戏

        private async void AddGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.ResetAddGameForm();

            var dialog = new ContentDialog
            {
                Title = "添加游戏",
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500
            };

            var panel = new StackPanel { Spacing = 12, MinWidth = 400 };

            // ========== 基本信息组 ==========
            panel.Children.Add(new TextBlock
            {
                Text = "基本信息",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

            // 游戏名称
            var nameBox = new TextBox
            {
                Header = "游戏名称 *",
                PlaceholderText = "例如: ELDEN RING"
            };
            panel.Children.Add(nameBox);

            // 存档目录
            var savePathPanel = new Grid();
            savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var savePathBox = new TextBox
            {
                Header = "游戏存档目录 *",
                PlaceholderText = "选择游戏存档所在的目录",
                IsReadOnly = true
            };
            Grid.SetColumn(savePathBox, 0);

            var browseSaveBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseSaveBtn.Click += async (s, args) =>
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    // 自动将绝对路径替换为环境变量形式，方便多设备导入导出
                    savePathBox.Text = PathEnvironmentHelper.ReplaceWithEnvVariables(folder.Path);
                }
            };
            Grid.SetColumn(browseSaveBtn, 1);

            savePathPanel.Children.Add(savePathBox);
            savePathPanel.Children.Add(browseSaveBtn);
            panel.Children.Add(savePathPanel);

            // 启动进程（可选）
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "游戏启动进程（可选）",
                PlaceholderText = "选择游戏可执行文件",
                IsReadOnly = true
            };
            Grid.SetColumn(processPathBox, 0);

            var browseProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseProcessBtn.Click += async (s, args) =>
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    processPathBox.Text = file.Path;
                }
            };
            Grid.SetColumn(browseProcessBtn, 1);

            processPathPanel.Children.Add(processPathBox);
            processPathPanel.Children.Add(browseProcessBtn);
            panel.Children.Add(processPathPanel);

            // 启动参数（可选）
            var argsBox = new TextBox
            {
                Header = "启动附加参数（可选）",
                PlaceholderText = "例如: -windowed -dx12"
            };
            panel.Children.Add(argsBox);

            // 云端服务商（可选）
            ComboBox? cloudConfigComboBox = null;
            if (ViewModel.CloudConfigs.Count > 0)
            {
                cloudConfigComboBox = new ComboBox
                {
                    Header = "云端服务商（可选）",
                    PlaceholderText = "不使用云端同步",
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    DisplayMemberPath = "DisplayName"
                };
                cloudConfigComboBox.ItemsSource = ViewModel.CloudConfigs;
                panel.Children.Add(cloudConfigComboBox);
            }

            // ========== 定时备份组 ==========
            // 分隔线
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 4)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "定时备份",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

            // 定时备份开关
            var scheduledBackupToggle = new ToggleSwitch
            {
                Header = "启用定时备份",
                IsOn = false,
                OnContent = "已启用",
                OffContent = "已关闭"
            };
            panel.Children.Add(scheduledBackupToggle);

            // 定时备份说明（根据是否填了进程动态显示）
            var scheduledBackupDesc = new TextBlock
            {
                Text = "有启动进程：游戏运行时自动开始，退出后停止\n无启动进程：在游戏列表中手动控制启停",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };
            panel.Children.Add(scheduledBackupDesc);

            // 备份间隔
            var intervalBox = new NumberBox
            {
                Header = "备份间隔（分钟）",
                Value = 30,
                Minimum = 1,
                Maximum = 1440,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };
            panel.Children.Add(intervalBox);

            // 最大备份数量
            var maxCountBox = new NumberBox
            {
                Header = "最大备份数量",
                Value = 5,
                Minimum = 1,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };
            panel.Children.Add(maxCountBox);

            // 开关控制子控件显示
            scheduledBackupToggle.Toggled += (s, args) =>
            {
                var vis = scheduledBackupToggle.IsOn
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
                scheduledBackupDesc.Visibility = vis;
                intervalBox.Visibility = vis;
                maxCountBox.Visibility = vis;
            };

            scrollViewer.Content = panel;
            dialog.Content = scrollViewer;

            var result = await dialog.ShowWithThemeAsync();

            if (result == ContentDialogResult.Primary)
            {
                ViewModel.NewGameName = nameBox.Text;
                ViewModel.NewGameSavePath = savePathBox.Text;
                ViewModel.NewGameProcessPath = processPathBox.Text;
                ViewModel.NewGameProcessArgs = argsBox.Text;

                // 设置选中的云端配置 ID
                if (cloudConfigComboBox?.SelectedItem is CloudConfig selectedConfig)
                {
                    ViewModel.SelectedCloudConfigId = selectedConfig.Id;
                }
                else
                {
                    ViewModel.SelectedCloudConfigId = null;
                }

                // 设置定时备份参数
                ViewModel.NewGameScheduledBackupEnabled = scheduledBackupToggle.IsOn;
                ViewModel.NewGameScheduledBackupInterval = (int)intervalBox.Value;
                ViewModel.NewGameScheduledBackupMaxCount = (int)maxCountBox.Value;

                var success = await ViewModel.AddGameAsync();
                if (success)
                {
                    UpdateEmptyState();
                }
                else
                {
                    await ShowMessageAsync("添加失败", ViewModel.StatusMessage);
                }
            }
        }

        #endregion

        #region 启动游戏

        private async void LaunchGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.SelectedGame == null) return;

            if (string.IsNullOrWhiteSpace(ViewModel.SelectedGame.ProcessPath))
            {
                await ShowMessageAsync("无法启动", "该游戏未设置启动进程路径，请编辑游戏信息后再试。");
                return;
            }

            var (success, message) = await ViewModel.LaunchGameAsync();
            if (success)
            {
                // 关闭详情面板，最小化窗口
                ViewModel.CloseDetails();
                MainPage.MinimizeWindow();
            }
            else
            {
                await ShowMessageAsync("启动失败", message);
            }
        }

        #endregion

        #region 手动备份

        private async void ManualBackup_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.SelectedGame == null) return;

            // 弹出输入名称对话框
            var nameBox = new TextBox
            {
                PlaceholderText = "留空则使用默认名称「手动存档」",
                Header = "存档名称（可选）"
            };

            var dialog = new ContentDialog
            {
                Title = "手动备份",
                Content = nameBox,
                PrimaryButtonText = "备份",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.ManualSaveName = nameBox.Text;
                var (success, message) = await ViewModel.ManualBackupAsync();
                if (!success)
                {
                    await ShowMessageAsync("备份失败", message);
                }
            }
        }

        #endregion

        #region 恢复存档

        private async void RestoreSave_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SaveFile save)
            {
                try
                {
                    var (success, message) = await ViewModel.RestoreSaveAsync(save);
                    if (!success)
                    {
                        await ShowMessageAsync("恢复失败", message);
                    }
                    else
                    {
                        await ShowMessageAsync("恢复成功", message);
                    }
                }
                catch (GameRunningException ex)
                {
                    // 二次确认弹窗
                    var confirmDialog = new ContentDialog
                    {
                        Title = "⚠️ 游戏正在运行",
                        Content = ex.Message,
                        PrimaryButtonText = "强制恢复",
                        SecondaryButtonText = "取消",
                        DefaultButton = ContentDialogButton.Secondary,
                        XamlRoot = this.XamlRoot
                    };

                    var confirmResult = await confirmDialog.ShowWithThemeAsync();
                    if (confirmResult == ContentDialogResult.Primary)
                    {
                        var (success, message) = await ViewModel.RestoreSaveAsync(save, force: true);
                        if (!success)
                        {
                            await ShowMessageAsync("恢复失败", message);
                        }
                    }
                }
            }
        }

        #endregion

        #region 删除存档

        private async void DeleteSave_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SaveFile save)
            {
                var dialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除存档「{save.Name}」吗？此操作不可撤回。",
                    PrimaryButtonText = "删除",
                    SecondaryButtonText = "取消",
                    DefaultButton = ContentDialogButton.Secondary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowWithThemeAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var (success, message) = await ViewModel.DeleteSaveAsync(save);
                    if (!success)
                    {
                        await ShowMessageAsync("删除失败", message);
                    }
                }
            }
        }

        #endregion

        #region 批量删除存档

        /// <summary>进入批量删除模式</summary>
        private void EnterBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SavesListView.SelectionMode = ListViewSelectionMode.Multiple;
            EnterBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ConfirmBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            CancelBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            BatchDeleteCountText.Text = "删除所选";
        }

        /// <summary>取消批量删除模式</summary>
        private void CancelBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ExitBatchDeleteMode();
        }

        /// <summary>执行批量删除</summary>
        private async void ConfirmBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var selectedItems = SavesListView.SelectedItems
                .OfType<SaveFile>()
                .ToList();

            if (selectedItems.Count == 0)
            {
                await ShowMessageAsync("提示", "请先选择要删除的存档");
                return;
            }

            // 过滤出可删除的（手动存档）
            var deletable = selectedItems.Where(s => s.CanDelete).ToList();
            var skipped = selectedItems.Count - deletable.Count;

            var confirmMsg = deletable.Count > 0
                ? $"确定要删除选中的 {deletable.Count} 个存档吗？" +
                  (skipped > 0 ? $"\n（已自动跳过 {skipped} 个退出存档）" : "") +
                  "\n\n此操作不可撤回。"
                : "所选存档均为退出存档，无法删除。";

            if (deletable.Count == 0)
            {
                await ShowMessageAsync("提示", confirmMsg);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "确认批量删除",
                Content = confirmMsg,
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                var (success, message) = await ViewModel.BatchDeleteSavesAsync(deletable);
                if (!success)
                {
                    await ShowMessageAsync("批量删除", message);
                }

                ExitBatchDeleteMode();
            }
        }

        /// <summary>选择变更时更新计数文字</summary>
        private void SavesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SavesListView.SelectionMode == ListViewSelectionMode.Multiple)
            {
                var count = SavesListView.SelectedItems.Count;
                BatchDeleteCountText.Text = count > 0
                    ? $"删除所选 ({count})"
                    : "删除所选";
            }
        }

        /// <summary>退出批量删除模式</summary>
        private void ExitBatchDeleteMode()
        {
            SavesListView.SelectionMode = ListViewSelectionMode.None;
            EnterBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ConfirmBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            CancelBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        #endregion

        #region 删除游戏

        private async void DeleteGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.SelectedGame == null) return;

            // 构建对话框内容：提示文字 + 云端删除选项
            var contentPanel = new StackPanel { Spacing = 12 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = $"确定要删除游戏「{ViewModel.SelectedGame.Name}」吗？\n该游戏的所有存档备份也将被清除，此操作不可撤回。",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });

            // 仅当游戏关联了云端配置时，显示删除云端存档的选项
            CheckBox? deleteCloudCheckBox = null;
            if (!string.IsNullOrEmpty(ViewModel.SelectedGame.CloudConfigId))
            {
                deleteCloudCheckBox = new CheckBox
                {
                    Content = "同时删除云端存档",
                    IsChecked = false
                };
                contentPanel.Children.Add(deleteCloudCheckBox);
            }

            var dialog = new ContentDialog
            {
                Title = "确认删除游戏",
                Content = contentPanel,
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                bool deleteCloud = deleteCloudCheckBox?.IsChecked == true;
                var (success, message) = await ViewModel.DeleteGameAsync(deleteCloud);
                if (success)
                {
                    UpdateEmptyState();
                }
                else
                {
                    await ShowMessageAsync("删除失败", message);
                }
            }
        }

        #endregion

        #region 辅助方法

        private async Task ShowMessageAsync(string title, string message)
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

        #endregion

        #region 右键菜单

        /// <summary>从右键菜单项获取绑定的 Game 对象</summary>
        private Game? GetGameFromContext(object sender)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Game game)
                return game;
            return null;
        }

        private async void ContextLaunch_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var game = GetGameFromContext(sender);
            if (game == null) return;

            if (string.IsNullOrWhiteSpace(game.ProcessPath))
            {
                await ShowMessageAsync("无法启动", "该游戏未设置启动进程路径，请编辑游戏信息后再试。");
                return;
            }

            ViewModel.SelectedGame = game;
            var (success, message) = await ViewModel.LaunchGameAsync();
            if (success)
            {
                ViewModel.CloseDetails();
                MainPage.MinimizeWindow();
            }
            else
            {
                await ShowMessageAsync("启动失败", message);
            }
        }

        private async void ContextBackup_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var game = GetGameFromContext(sender);
            if (game == null) return;

            ViewModel.SelectedGame = game;

            var nameBox = new TextBox
            {
                PlaceholderText = "留空则使用默认名称「手动存档」",
                Header = "存档名称（可选）"
            };

            var dialog = new ContentDialog
            {
                Title = $"备份 {game.Name}",
                Content = nameBox,
                PrimaryButtonText = "备份",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.ManualSaveName = nameBox.Text;
                var (success, msg) = await ViewModel.ManualBackupAsync();
                if (!success)
                {
                    await ShowMessageAsync("备份失败", msg);
                }
            }
        }

        private async void ContextEdit_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var game = GetGameFromContext(sender);
            if (game == null) return;

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500
            };

            var panel = new StackPanel { Spacing = 12, MinWidth = 400 };

            // ========== 基本信息组 ==========
            panel.Children.Add(new TextBlock
            {
                Text = "基本信息",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });
            var nameBox = new TextBox
            {
                Header = "游戏名称 *",
                PlaceholderText = "例如: ELDEN RING",
                Text = game.Name
            };
            panel.Children.Add(nameBox);

            // 存档目录（只读，不允许更改）
            var savePathBox = new TextBox
            {
                Header = "游戏存档目录（不可更改）",
                Text = game.ResolvedSaveFolderPath,
                IsReadOnly = true,
                IsEnabled = false
            };
            panel.Children.Add(savePathBox);

            // 启动进程（可选）
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "游戏启动进程（可选）",
                PlaceholderText = "选择游戏可执行文件",
                IsReadOnly = true,
                Text = game.ProcessPath ?? string.Empty
            };
            Grid.SetColumn(processPathBox, 0);

            var browseProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseProcessBtn.Click += async (s, args) =>
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    processPathBox.Text = file.Path;
                }
            };
            Grid.SetColumn(browseProcessBtn, 1);

            processPathPanel.Children.Add(processPathBox);
            processPathPanel.Children.Add(browseProcessBtn);
            panel.Children.Add(processPathPanel);

            // 启动参数（可选）
            var argsBox = new TextBox
            {
                Header = "启动附加参数（可选）",
                PlaceholderText = "例如: -windowed -dx12",
                Text = game.ProcessArgs ?? string.Empty
            };
            panel.Children.Add(argsBox);

            // 云端服务商（可选）
            ComboBox? cloudConfigComboBox = null;
            if (ViewModel.CloudConfigs.Count > 0)
            {
                cloudConfigComboBox = new ComboBox
                {
                    Header = "云端服务商（可选）",
                    PlaceholderText = "不使用云端同步",
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    DisplayMemberPath = "DisplayName"
                };
                cloudConfigComboBox.ItemsSource = ViewModel.CloudConfigs;

                // 预选当前游戏关联的云端配置
                if (!string.IsNullOrEmpty(game.CloudConfigId))
                {
                    foreach (var config in ViewModel.CloudConfigs)
                    {
                        if (config.Id == game.CloudConfigId)
                        {
                            cloudConfigComboBox.SelectedItem = config;
                            break;
                        }
                    }
                }

                panel.Children.Add(cloudConfigComboBox);
            }

            // ========== 定时备份组 ==========
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 4)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "定时备份",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

            var scheduledBackupToggle = new ToggleSwitch
            {
                Header = "启用定时备份",
                IsOn = game.ScheduledBackupEnabled,
                OnContent = "已启用",
                OffContent = "已关闭"
            };
            panel.Children.Add(scheduledBackupToggle);

            var editScheduledBackupDesc = new TextBlock
            {
                Text = "有启动进程：游戏运行时自动开始，退出后停止\n无启动进程：在游戏列表中手动控制启停",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Visibility = game.ScheduledBackupEnabled
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed
            };
            panel.Children.Add(editScheduledBackupDesc);

            var editInitialVis = game.ScheduledBackupEnabled
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

            var editIntervalBox = new NumberBox
            {
                Header = "备份间隔（分钟）",
                Value = game.ScheduledBackupIntervalMinutes,
                Minimum = 1,
                Maximum = 1440,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = editInitialVis
            };
            panel.Children.Add(editIntervalBox);

            var editMaxCountBox = new NumberBox
            {
                Header = "最大备份数量",
                Value = game.ScheduledBackupMaxCount,
                Minimum = 1,
                Maximum = 100,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Visibility = editInitialVis
            };
            panel.Children.Add(editMaxCountBox);

            scheduledBackupToggle.Toggled += (s, args) =>
            {
                var vis = scheduledBackupToggle.IsOn
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
                editScheduledBackupDesc.Visibility = vis;
                editIntervalBox.Visibility = vis;
                editMaxCountBox.Visibility = vis;
            };

            scrollViewer.Content = panel;

            var dialog = new ContentDialog
            {
                Title = $"编辑游戏 - {game.Name}",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = scrollViewer
            };

            var result = await dialog.ShowWithThemeAsync();

            if (result == ContentDialogResult.Primary)
            {
                // 更新游戏属性（存档目录不变）
                game.Name = nameBox.Text.Trim();
                game.ProcessPath = string.IsNullOrWhiteSpace(processPathBox.Text) ? null : processPathBox.Text.Trim();
                game.ProcessArgs = string.IsNullOrWhiteSpace(argsBox.Text) ? null : argsBox.Text.Trim();

                if (cloudConfigComboBox?.SelectedItem is CloudConfig selectedConfig)
                {
                    game.CloudConfigId = selectedConfig.Id;
                }
                else
                {
                    game.CloudConfigId = null;
                }

                // 更新定时备份设置
                game.ScheduledBackupEnabled = scheduledBackupToggle.IsOn;
                game.ScheduledBackupIntervalMinutes = (int)editIntervalBox.Value;
                game.ScheduledBackupMaxCount = (int)editMaxCountBox.Value;

                var (success, message) = await ViewModel.UpdateGameAsync(game);
                if (!success)
                {
                    await ShowMessageAsync("编辑失败", message);
                }
            }
        }

        private async void ContextDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var game = GetGameFromContext(sender);
            if (game == null) return;

            // 构建对话框内容：提示文字 + 云端删除选项
            var contentPanel = new StackPanel { Spacing = 12 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = $"确定要删除游戏「{game.Name}」吗？\n该游戏的所有存档备份也将被清除，此操作不可撤回。",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });

            // 仅当游戏关联了云端配置时，显示删除云端存档的选项
            CheckBox? deleteCloudCheckBox = null;
            if (!string.IsNullOrEmpty(game.CloudConfigId))
            {
                deleteCloudCheckBox = new CheckBox
                {
                    Content = "同时删除云端存档",
                    IsChecked = false
                };
                contentPanel.Children.Add(deleteCloudCheckBox);
            }

            var dialog = new ContentDialog
            {
                Title = "确认删除游戏",
                Content = contentPanel,
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                bool deleteCloud = deleteCloudCheckBox?.IsChecked == true;
                // 使用直接传递游戏对象的重载，避免设置 SelectedGame 触发详情面板显示存档列表
                var (success, message) = await ViewModel.DeleteGameAsync(game, deleteCloud);
                if (success)
                {
                    UpdateEmptyState();
                }
                else
                {
                    await ShowMessageAsync("删除失败", message);
                }
            }
        }

        #endregion
    }
}
