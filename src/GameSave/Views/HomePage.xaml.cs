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
            if (ViewModel.Games.Count == 0)
            {
                // 没有任何游戏
                EmptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                // 恢复默认提示文本
                if (EmptyStatePanel.Children.Count >= 2 && EmptyStatePanel.Children[1] is TextBlock titleText)
                {
                    titleText.Text = "还没有添加任何游戏";
                }
                if (EmptyStatePanel.Children.Count >= 3 && EmptyStatePanel.Children[2] is TextBlock subtitleText)
                {
                    subtitleText.Text = "点击上方「添加游戏」开始管理你的存档";
                }
            }
            else if (ViewModel.FilteredGames.Count == 0)
            {
                // 有游戏但搜索无结果
                EmptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                if (EmptyStatePanel.Children.Count >= 2 && EmptyStatePanel.Children[1] is TextBlock titleText)
                {
                    titleText.Text = "未找到匹配的游戏";
                }
                if (EmptyStatePanel.Children.Count >= 3 && EmptyStatePanel.Children[2] is TextBlock subtitleText)
                {
                    subtitleText.Text = "请尝试其他关键字";
                }
            }
            else
            {
                EmptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
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

        #region 搜索游戏

        /// <summary>搜索框文本变化时实时过滤游戏列表</summary>
        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // 仅处理用户输入引起的文本变更
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.SearchKeyword = sender.Text;
                UpdateEmptyState();
            }
        }

        /// <summary>搜索框提交查询</summary>
        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ViewModel.SearchKeyword = args.QueryText;
            UpdateEmptyState();
        }

        #endregion

        #region 游戏列表项交互

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
                    if (success)
                    {
                        // 启动成功，隐藏到系统托盘并发送通知
                        App.HideToTrayForGame(game.Name);
                    }
                    else
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

            // ========== 多存档目录区域 ==========
            var savePathsContainer = new StackPanel { Spacing = 8 };
            var savePathRows = new List<(Grid row, TextBox textBox)>();

            // 存档目录标题行（含加号按钮）
            var savePathHeaderPanel = new Grid();
            savePathHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            savePathHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var savePathHeader = new TextBlock
            {
                Text = "游戏存档目录 *",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            Grid.SetColumn(savePathHeader, 0);

            var addPathBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE710", FontSize = 12 },
                        new TextBlock { Text = "添加目录", FontSize = 12 }
                    }
                },
                Padding = new Microsoft.UI.Xaml.Thickness(8, 4, 8, 4)
            };
            Grid.SetColumn(addPathBtn, 1);

            savePathHeaderPanel.Children.Add(savePathHeader);
            savePathHeaderPanel.Children.Add(addPathBtn);
            panel.Children.Add(savePathHeaderPanel);
            panel.Children.Add(savePathsContainer);

            // 添加存档路径行的方法
            void AddSavePathRow(string initialPath = "")
            {
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathBox = new TextBox
                {
                    PlaceholderText = "输入路径或点击浏览选择目录",
                    Text = initialPath
                };
                Grid.SetColumn(pathBox, 0);

                var browseBtn = new Button
                {
                    Content = "浏览",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseBtn.Click += async (s, args) =>
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                    picker.FileTypeFilter.Add("*");

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        pathBox.Text = PathEnvironmentHelper.ReplaceWithEnvVariables(folder.Path);
                    }
                };
                Grid.SetColumn(browseBtn, 1);

                var removeBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) },
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0),
                    Padding = new Microsoft.UI.Xaml.Thickness(6),
                    // 只有一行时不允许删除
                    Visibility = savePathRows.Count == 0
                        ? Microsoft.UI.Xaml.Visibility.Collapsed
                        : Microsoft.UI.Xaml.Visibility.Visible
                };
                var currentRow = rowGrid;
                var currentPathBox = pathBox;
                removeBtn.Click += (s, args) =>
                {
                    savePathsContainer.Children.Remove(currentRow);
                    savePathRows.RemoveAll(r => r.row == currentRow);
                    // 如果只剩一行，隐藏其删除按钮
                    if (savePathRows.Count == 1)
                    {
                        var lastRow = savePathRows[0].row;
                        // 找到删除按钮（第三个子元素）
                        if (lastRow.Children.Count >= 3)
                        {
                            ((FrameworkElement)lastRow.Children[2]).Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        }
                    }
                };
                Grid.SetColumn(removeBtn, 2);

                rowGrid.Children.Add(pathBox);
                rowGrid.Children.Add(browseBtn);
                rowGrid.Children.Add(removeBtn);

                savePathsContainer.Children.Add(rowGrid);
                savePathRows.Add((rowGrid, pathBox));

                // 添加新行后，更新所有行的删除按钮可见性
                if (savePathRows.Count > 1)
                {
                    foreach (var (row, _) in savePathRows)
                    {
                        if (row.Children.Count >= 3)
                        {
                            ((FrameworkElement)row.Children[2]).Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        }
                    }
                }
            }

            // 默认添加一行
            AddSavePathRow();

            // 加号按钮点击事件
            addPathBtn.Click += (s, args) => AddSavePathRow();

            // 启动进程（可选）
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "游戏启动进程（可选）",
                PlaceholderText = "输入路径或点击浏览选择文件"
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
                ViewModel.NewGameSavePaths = savePathRows
                    .Select(r => r.textBox.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
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

        /// <summary>
        /// 导入游戏按钮点击：扫描本地已安装游戏，弹出批量导入弹窗
        /// </summary>
        private async void ImportGames_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // 1. 显示扫描中提示
            var scanningDialog = new ContentDialog
            {
                Title = "正在扫描本地游戏...",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new ProgressRing { IsActive = true, Width = 40, Height = 40, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center },
                        new TextBlock { Text = "正在检测 Steam、Epic、GOG、Ubisoft、EA、Battle.net 平台...", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center }
                    }
                },
                XamlRoot = this.XamlRoot
            };

            // 异步扫描
            var scanner = new GameScannerService();
            List<DetectedGame>? detectedGames = null;

            // 自动关闭扫描弹窗
            _ = Task.Run(async () =>
            {
                detectedGames = await scanner.ScanAllPlatformsAsync();

                // 过滤掉已导入的游戏（按 exe 路径或安装路径匹配）
                var existingPaths = ViewModel.Games
                    .Select(g => g.ProcessPath?.ToLowerInvariant().TrimEnd('\\', '/'))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToHashSet();

                var existingInstallBases = ViewModel.Games
                    .Select(g => g.ProcessPath != null ? Path.GetDirectoryName(g.ProcessPath)?.ToLowerInvariant().TrimEnd('\\', '/') : null)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToHashSet();

                detectedGames = detectedGames
                    .Where(d =>
                    {
                        // 按 exe 路径排重
                        if (!string.IsNullOrEmpty(d.ExePath) &&
                            existingPaths.Contains(d.ExePath.ToLowerInvariant().TrimEnd('\\', '/')))
                            return false;

                        // 按安装路径排重
                        if (existingInstallBases.Contains(d.InstallPath.ToLowerInvariant().TrimEnd('\\', '/')))
                            return false;

                        return true;
                    })
                    .ToList();

                App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                {
                    scanningDialog.Hide();
                });
            });

            await scanningDialog.ShowWithThemeAsync();

            // 2. 检查扫描结果
            if (detectedGames == null || detectedGames.Count == 0)
            {
                await ShowMessageAsync("扫描完成", "未发现新的本地游戏，可能所有检测到的游戏已经被添加。");
                return;
            }

            // 3. 构建批量导入弹窗
            var importDialog = new ContentDialog
            {
                Title = $"发现 {detectedGames.Count} 个本地游戏",
                PrimaryButtonText = "导入所选",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500
            };

            var mainPanel = new StackPanel { Spacing = 8, MinWidth = 500 };

            // 全选/取消全选
            var selectAllCheckBox = new CheckBox
            {
                Content = "全选 / 取消全选",
                IsChecked = true,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8)
            };
            mainPanel.Children.Add(selectAllCheckBox);

            // 为每个游戏构建一个 Expander
            var gameExpanders = new List<(Expander expander, DetectedGame game, TextBox savePathBox, TextBox processArgsBox, ComboBox? cloudCombo, ToggleSwitch scheduledToggle, NumberBox intervalBox, NumberBox maxCountBox)>();

            foreach (var detected in detectedGames)
            {
                // ---- Expander Header：CheckBox + 游戏名 + 来源徽章 ----
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };

                var gameCheckBox = new CheckBox { IsChecked = true, MinWidth = 0, Padding = new Microsoft.UI.Xaml.Thickness(0) };
                // 双向绑定到 detected.IsSelected
                gameCheckBox.Checked += (s, args) => detected.IsSelected = true;
                gameCheckBox.Unchecked += (s, args) => detected.IsSelected = false;
                headerPanel.Children.Add(gameCheckBox);

                headerPanel.Children.Add(new TextBlock
                {
                    Text = detected.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    MaxWidth = 280,
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
                });

                // 来源徽章
                headerPanel.Children.Add(new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        detected.Source switch
                        {
                            "Steam" => Windows.UI.Color.FromArgb(255, 27, 40, 56),
                            "Epic" => Windows.UI.Color.FromArgb(255, 45, 45, 45),
                            "GOG" => Windows.UI.Color.FromArgb(255, 102, 46, 155),
                            "Ubisoft" => Windows.UI.Color.FromArgb(255, 0, 98, 175),
                            "EA" => Windows.UI.Color.FromArgb(255, 0, 100, 0),
                            "Battle.net" => Windows.UI.Color.FromArgb(255, 0, 108, 190),
                            _ => Windows.UI.Color.FromArgb(255, 100, 100, 100)
                        }),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                    Padding = new Microsoft.UI.Xaml.Thickness(8, 2, 8, 2),
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    Child = new TextBlock { Text = detected.Source, FontSize = 11, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) }
                });

                // ---- Expander Content：表单字段 ----
                var contentPanel = new StackPanel { Spacing = 10, Padding = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0) };

                // 游戏名称（可编辑）
                var nameBox = new TextBox { Header = "游戏名称", Text = detected.Name };
                nameBox.TextChanged += (s, args) => detected.Name = nameBox.Text;
                contentPanel.Children.Add(nameBox);

                // 启动进程（已自动填充）
                var exeInfo = new TextBox
                {
                    Header = "启动进程（已自动检测）",
                    Text = detected.ExePath ?? "未检测到",
                    IsReadOnly = true,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        string.IsNullOrEmpty(detected.ExePath) ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.Gray)
                };
                contentPanel.Children.Add(exeInfo);

                // 存档目录（必须用户手动选择）
                var savePathPanel = new Grid();
                savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var savePathBox = new TextBox
                {
                    Header = "游戏存档目录 *",
                    PlaceholderText = "输入路径或点击浏览选择目录"
                };
                Grid.SetColumn(savePathBox, 0);

                // 手动输入时同步更新 detected.SaveFolderPath
                savePathBox.TextChanged += (s, args) =>
                {
                    detected.SaveFolderPath = savePathBox.Text;
                };

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
                        savePathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(folder.Path);
                        detected.SaveFolderPath = savePathBox.Text;
                    }
                };
                Grid.SetColumn(browseSaveBtn, 1);

                savePathPanel.Children.Add(savePathBox);
                savePathPanel.Children.Add(browseSaveBtn);
                contentPanel.Children.Add(savePathPanel);

                // 启动参数
                var processArgsBox = new TextBox
                {
                    Header = "启动附加参数（可选）",
                    PlaceholderText = "例如: -windowed -dx12"
                };
                processArgsBox.TextChanged += (s, args) => detected.ProcessArgs = processArgsBox.Text;
                contentPanel.Children.Add(processArgsBox);

                // 云端服务商
                ComboBox? cloudCombo = null;
                if (ViewModel.CloudConfigs.Count > 0)
                {
                    cloudCombo = new ComboBox
                    {
                        Header = "云端服务商（可选）",
                        PlaceholderText = "不使用云端同步",
                        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                        DisplayMemberPath = "DisplayName"
                    };
                    cloudCombo.ItemsSource = ViewModel.CloudConfigs;
                    cloudCombo.SelectionChanged += (s, args) =>
                    {
                        detected.CloudConfigId = (cloudCombo.SelectedItem as CloudConfig)?.Id;
                    };
                    contentPanel.Children.Add(cloudCombo);
                }

                // 定时备份开关
                var scheduledToggle = new ToggleSwitch
                {
                    Header = "启用定时备份",
                    IsOn = false,
                    OnContent = "已启用",
                    OffContent = "已关闭"
                };
                contentPanel.Children.Add(scheduledToggle);

                var intervalBox = new NumberBox
                {
                    Header = "备份间隔（分钟）",
                    Value = 30,
                    Minimum = 1,
                    Maximum = 1440,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
                };
                contentPanel.Children.Add(intervalBox);

                var maxCountBox = new NumberBox
                {
                    Header = "最大备份数量",
                    Value = 5,
                    Minimum = 1,
                    Maximum = 100,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
                };
                contentPanel.Children.Add(maxCountBox);

                scheduledToggle.Toggled += (s, args) =>
                {
                    var vis = scheduledToggle.IsOn ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
                    intervalBox.Visibility = vis;
                    maxCountBox.Visibility = vis;
                    detected.ScheduledBackupEnabled = scheduledToggle.IsOn;
                };

                // 创建 Expander
                var expander = new Expander
                {
                    Header = headerPanel,
                    Content = contentPanel,
                    IsExpanded = false,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
                };

                mainPanel.Children.Add(expander);
                gameExpanders.Add((expander, detected, savePathBox, processArgsBox, cloudCombo, scheduledToggle, intervalBox, maxCountBox));
            }

            // 全选/取消全选逻辑
            selectAllCheckBox.Checked += (s, args) =>
            {
                foreach (var (_, game, _, _, _, _, _, _) in gameExpanders)
                {
                    game.IsSelected = true;
                }
                // 更新所有 CheckBox
                foreach (var expanderItem in gameExpanders)
                {
                    if (expanderItem.expander.Header is StackPanel hp)
                    {
                        var cb = hp.Children.OfType<CheckBox>().FirstOrDefault();
                        if (cb != null) cb.IsChecked = true;
                    }
                }
            };
            selectAllCheckBox.Unchecked += (s, args) =>
            {
                foreach (var (_, game, _, _, _, _, _, _) in gameExpanders)
                {
                    game.IsSelected = false;
                }
                foreach (var expanderItem in gameExpanders)
                {
                    if (expanderItem.expander.Header is StackPanel hp)
                    {
                        var cb = hp.Children.OfType<CheckBox>().FirstOrDefault();
                        if (cb != null) cb.IsChecked = false;
                    }
                }
            };

            scrollViewer.Content = mainPanel;
            importDialog.Content = scrollViewer;

            // 使用 Closing 事件拦截验证：点击「导入所选」时检查是否有未补全的必填信息
            importDialog.Closing += (s, args) =>
            {
                // 仅拦截点击主按钮（导入所选）时的关闭
                if (args.Result != ContentDialogResult.Primary)
                    return;

                // 查找勾选但未填写存档目录的游戏
                var incomplete = gameExpanders
                    .Where(item => item.game.IsSelected && string.IsNullOrWhiteSpace(item.savePathBox.Text))
                    .ToList();

                if (incomplete.Count > 0)
                {
                    // 阻止弹窗关闭
                    args.Cancel = true;

                    // 先折叠所有 Expander，再展开第一个未补全的
                    foreach (var item in gameExpanders)
                    {
                        item.expander.IsExpanded = false;
                    }

                    var first = incomplete[0];
                    first.expander.IsExpanded = true;

                    // 高亮存档目录输入框边框为红色提醒
                    foreach (var item in incomplete)
                    {
                        item.expander.IsExpanded = true;
                        item.savePathBox.Header = "游戏存档目录 * （请补全此项）";
                        item.savePathBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                        item.savePathBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(2);
                    }

                    // 聚焦到第一个未补全的存档目录输入框
                    first.savePathBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                }
            };

            var result = await importDialog.ShowWithThemeAsync();

            if (result == ContentDialogResult.Primary)
            {
                // 收集用户填写的定时备份参数
                foreach (var (_, detected, _, _, _, scheduledToggle, intervalBox, maxCountBox) in gameExpanders)
                {
                    detected.ScheduledBackupIntervalMinutes = (int)intervalBox.Value;
                    detected.ScheduledBackupMaxCount = (int)maxCountBox.Value;
                }

                // 过滤出勾选的游戏
                var selectedGames = detectedGames.Where(g => g.IsSelected).ToList();

                if (selectedGames.Count == 0)
                {
                    await ShowMessageAsync("提示", "未勾选任何游戏");
                    return;
                }

                var (successCount, failCount, message) = await ViewModel.BatchAddGamesAsync(selectedGames);
                UpdateEmptyState();

                if (failCount > 0)
                {
                    await ShowMessageAsync("导入结果", message);
                }
                else if (successCount > 0)
                {
                    await ShowMessageAsync("导入成功", message);
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

            var gameName = ViewModel.SelectedGame.Name;
            var (success, message) = await ViewModel.LaunchGameAsync();
            if (success)
            {
                // 关闭详情面板，隐藏到系统托盘并发送通知
                ViewModel.CloseDetails();
                App.HideToTrayForGame(gameName);
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
                // 构建对话框内容：提示文字 + 云端删除选项
                var contentPanel = new StackPanel { Spacing = 12 };
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"确定要删除存档「{save.Name}」吗？此操作不可撤回。",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                });

                // 仅当游戏关联了云端配置时，显示删除云端存档的选项
                CheckBox? deleteCloudCheckBox = null;
                if (ViewModel.SelectedGame != null && !string.IsNullOrEmpty(ViewModel.SelectedGame.CloudConfigId))
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
                    Title = "确认删除",
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
                    var (success, message) = await ViewModel.DeleteSaveAsync(save, deleteCloud);
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

            // 构建对话框内容：提示文字 + 云端删除选项
            var contentPanel = new StackPanel { Spacing = 12 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = confirmMsg,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });

            // 仅当游戏关联了云端配置时，显示删除云端存档的选项
            CheckBox? deleteCloudCheckBox = null;
            if (ViewModel.SelectedGame != null && !string.IsNullOrEmpty(ViewModel.SelectedGame.CloudConfigId))
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
                Title = "确认批量删除",
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
                var (success, message) = await ViewModel.BatchDeleteSavesAsync(deletable, deleteCloud);
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

            var gameName = game.Name;
            ViewModel.SelectedGame = game;
            var (success, message) = await ViewModel.LaunchGameAsync();
            if (success)
            {
                ViewModel.CloseDetails();
                App.HideToTrayForGame(gameName);
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

            // 保存原始存档路径，用于后续比较是否有修改
            var originalSavePaths = new List<string>(game.SaveFolderPaths);

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

            // ========== 多存档目录区域（可编辑） ==========
            var editSavePathsContainer = new StackPanel { Spacing = 8 };
            var editSavePathRows = new List<(Grid row, TextBox textBox)>();

            // 存档目录标题行（含加号按钮和提醒文字）
            var editSavePathHeaderPanel = new Grid();
            editSavePathHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editSavePathHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var editSavePathHeader = new TextBlock
            {
                Text = "游戏存档目录",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            Grid.SetColumn(editSavePathHeader, 0);

            var editAddPathBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE710", FontSize = 12 },
                        new TextBlock { Text = "添加目录", FontSize = 12 }
                    }
                },
                Padding = new Microsoft.UI.Xaml.Thickness(8, 4, 8, 4)
            };
            Grid.SetColumn(editAddPathBtn, 1);

            editSavePathHeaderPanel.Children.Add(editSavePathHeader);
            editSavePathHeaderPanel.Children.Add(editAddPathBtn);
            panel.Children.Add(editSavePathHeaderPanel);

            // 修改提示
            panel.Children.Add(new TextBlock
            {
                Text = "⚠️ 修改存档路径可能导致已有备份存档无法正常还原",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });

            panel.Children.Add(editSavePathsContainer);

            // 添加存档路径行的方法（编辑模式）
            void AddEditSavePathRow(string initialPath = "")
            {
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathBox = new TextBox
                {
                    PlaceholderText = "输入路径或点击浏览选择目录",
                    Text = initialPath
                };
                Grid.SetColumn(pathBox, 0);

                var browseBtn = new Button
                {
                    Content = "浏览",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseBtn.Click += async (s, args) =>
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                    picker.FileTypeFilter.Add("*");

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        pathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(folder.Path);
                    }
                };
                Grid.SetColumn(browseBtn, 1);

                var removeBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) },
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0),
                    Padding = new Microsoft.UI.Xaml.Thickness(6),
                    Visibility = editSavePathRows.Count == 0
                        ? Microsoft.UI.Xaml.Visibility.Collapsed
                        : Microsoft.UI.Xaml.Visibility.Visible
                };
                var currentRow = rowGrid;
                removeBtn.Click += (s, args) =>
                {
                    editSavePathsContainer.Children.Remove(currentRow);
                    editSavePathRows.RemoveAll(r => r.row == currentRow);
                    if (editSavePathRows.Count == 1)
                    {
                        var lastRow = editSavePathRows[0].row;
                        if (lastRow.Children.Count >= 3)
                        {
                            ((FrameworkElement)lastRow.Children[2]).Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        }
                    }
                };
                Grid.SetColumn(removeBtn, 2);

                rowGrid.Children.Add(pathBox);
                rowGrid.Children.Add(browseBtn);
                rowGrid.Children.Add(removeBtn);

                editSavePathsContainer.Children.Add(rowGrid);
                editSavePathRows.Add((rowGrid, pathBox));

                if (editSavePathRows.Count > 1)
                {
                    foreach (var (row, _) in editSavePathRows)
                    {
                        if (row.Children.Count >= 3)
                        {
                            ((FrameworkElement)row.Children[2]).Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        }
                    }
                }
            }

            // 加载现有存档路径
            if (game.SaveFolderPaths.Count > 0)
            {
                foreach (var path in game.SaveFolderPaths)
                {
                    AddEditSavePathRow(path);
                }
            }
            else
            {
                AddEditSavePathRow();
            }

            editAddPathBtn.Click += (s, args) => AddEditSavePathRow();

            // 启动进程（可选）
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "游戏启动进程（可选）",
                PlaceholderText = "输入路径或点击浏览选择文件",
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
                // 收集新的存档路径
                var newSavePaths = editSavePathRows
                    .Select(r => r.textBox.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                // 检查存档路径是否有变化，如果有则进行二次确认
                bool pathsChanged = !originalSavePaths.SequenceEqual(newSavePaths);

                if (pathsChanged && newSavePaths.Count > 0)
                {
                    var confirmDialog = new ContentDialog
                    {
                        Title = "⚠️ 确认修改存档路径",
                        Content = "你修改了游戏存档路径。\n\n修改后，已有的备份存档可能无法正常还原到新路径。\n请确保你了解此操作的影响。\n\n确定要修改吗？",
                        PrimaryButtonText = "确认修改",
                        SecondaryButtonText = "取消",
                        DefaultButton = ContentDialogButton.Secondary,
                        XamlRoot = this.XamlRoot
                    };

                    var confirmResult = await confirmDialog.ShowWithThemeAsync();
                    if (confirmResult != ContentDialogResult.Primary)
                    {
                        return; // 用户取消，不保存
                    }
                }

                // 更新游戏属性
                game.Name = nameBox.Text.Trim();
                game.SaveFolderPaths = newSavePaths;
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
                if (success)
                {
                    // 刷新图标缓存（ProcessPath 可能已变更）
                    game.RefreshIcon();
                }
                else
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
