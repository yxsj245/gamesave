using GameSave.Models;
using GameSave.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace GameSave.Views
{
    /// <summary>
    /// 云端存档页面
    /// 展示云端存档列表（按游戏分组），支持下载、删除和导入恢复
    /// </summary>
    public partial class CloudPage : Page
    {
        public CloudViewModel ViewModel { get; } = new CloudViewModel();

        /// <summary>是否处于批量删除模式</summary>
        private bool _isCloudBatchMode = false;
        /// <summary>批量选中的云端存档列表</summary>
        private readonly List<SaveFile> _selectedCloudSaves = new();

        public CloudPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.LoadCloudConfigs();

            // 绑定配置下拉框
            CloudConfigComboBox.ItemsSource = ViewModel.CloudConfigs;

            UpdateVisibilityStates();

            // 自动选择第一个配置并刷新
            if (ViewModel.CloudConfigs.Count > 0)
            {
                CloudConfigComboBox.SelectedIndex = 0;
            }

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.StatusMessage))
            {
                // Ensure UI thread update
                DispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = ViewModel.StatusMessage;
                });
            }
        }

        /// <summary>
        /// 配置下拉框选择变更
        /// </summary>
        private async void CloudConfigComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CloudConfigComboBox.SelectedItem is CloudConfig config)
            {
                ViewModel.SelectedCloudConfig = config;
                await RefreshCloudSaves();
            }
        }

        /// <summary>
        /// 刷新按钮点击
        /// </summary>
        private async void Refresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await RefreshCloudSaves();
        }

        /// <summary>
        /// 前往设置页面
        /// </summary>
        private void GoToSettings_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // 通过导航到设置页面实现
            if (this.Frame?.Parent is NavigationView navView)
            {
                // 选中导航栏的设置项
                navView.SelectedItem = navView.SettingsItem;
            }
            else
            {
                this.Frame?.Navigate(typeof(SettingsPage));
            }
        }

        /// <summary>
        /// 刷新云端存档列表并更新 UI
        /// </summary>
        private async Task RefreshCloudSaves()
        {
            if (ViewModel.SelectedCloudConfig == null)
                return;

            // 状态切换
            ShowLoadingState();

            await ViewModel.RefreshAsync();

            // 构建 UI
            BuildSaveGroupsUI();

            // 状态切换
            if (ViewModel.SaveGroups.Count == 0)
            {
                ShowEmptyState();
            }
            else
            {
                ShowContentState();
            }

            StatusText.Text = ViewModel.StatusMessage;
        }

        /// <summary>
        /// 根据 ViewModel 的 SaveGroups 动态构建分组 UI
        /// </summary>
        private void BuildSaveGroupsUI()
        {
            var groupPanels = new StackPanel { Spacing = 16 };
            groupPanels.ChildrenTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection
            {
                new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { IsStaggeringEnabled = true }
            };

            foreach (var group in ViewModel.SaveGroups)
            {
                var expander = new Expander
                {
                    IsExpanded = false,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
                };

                // 游戏名称标题行
                var headerPanel = new Grid();
                headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
                headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });

                // 左侧：图标 + 名称 + 存档数量
                var leftPanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
                leftPanel.Children.Add(new FontIcon
                {
                    Glyph = "\uE7FC",
                    FontSize = 18,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                });
                leftPanel.Children.Add(new TextBlock
                {
                    Text = group.GameName,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                });
                leftPanel.Children.Add(new TextBlock
                {
                    Text = group.DisplayCount,
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
                });

                Grid.SetColumn(leftPanel, 0);
                headerPanel.Children.Add(leftPanel);

                // 右侧：操作按钮区域（导入恢复 + 删除整个游戏）
                var rightPanel = new StackPanel
                {
                    Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                };

                if (!group.IsLocalGameExists && group.CloudGameMetadata != null)
                {
                    var importBtn = new Button
                    {
                        Content = new StackPanel
                        {
                            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                            Spacing = 6,
                            Children =
                            {
                                new FontIcon { Glyph = "\uE896", FontSize = 14 },
                                new TextBlock { Text = "导入恢复", FontSize = 13 }
                            }
                        },
                        Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["AccentButtonStyle"],
                        Tag = group
                    };
                    importBtn.Click += ImportGame_Click;
                    rightPanel.Children.Add(importBtn);
                }
                else if (!group.IsLocalGameExists)
                {
                    // 没有云端元数据时显示提示文字
                    var hintText = new TextBlock
                    {
                        Text = "⚠ 缺少游戏信息，无法导入",
                        FontSize = 12,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                    };
                    rightPanel.Children.Add(hintText);
                }

                // 删除整个游戏按钮
                var deleteGameBtn = new Button
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Content = new StackPanel
                    {
                        Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE74D",
                                FontSize = 13,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                            },
                            new TextBlock
                            {
                                Text = "删除整个游戏",
                                FontSize = 12,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                            }
                        }
                    },
                    Tag = group
                };
                ToolTipService.SetToolTip(deleteGameBtn, "删除该游戏在云端的所有存档和元数据");
                deleteGameBtn.Click += DeleteGameFromCloud_Click;
                rightPanel.Children.Add(deleteGameBtn);

                Grid.SetColumn(rightPanel, 1);
                headerPanel.Children.Add(rightPanel);

                expander.Header = headerPanel;

                var savesContent = new StackPanel { Spacing = 12, Padding = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0) };
                savesContent.ChildrenTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection
                {
                    new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { IsStaggeringEnabled = true }
                };

                // 存档项列表
                foreach (var save in group.Saves)
                {
                    var saveItem = CreateSaveItemUI(save, group);
                    savesContent.Children.Add(saveItem);
                }

                expander.Content = savesContent;
                groupPanels.Children.Add(expander);
            }

            // 用一个手动的方式替换 ItemsRepeater（因为动态数据模板在代码后端更灵活）
            ContentScrollViewer.Content = groupPanels;
        }

        /// <summary>
        /// 创建单个存档项的 UI 元素
        /// </summary>
        private UIElement CreateSaveItemUI(SaveFile save, CloudSaveGroup group)
        {
            var grid = new Grid
            {
                Padding = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 6)
            };

            // 批量模式下增加 CheckBox 列
            if (_isCloudBatchMode)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });

            int colOffset = 0;

            // 批量模式：CheckBox
            if (_isCloudBatchMode)
            {
                var checkBox = new CheckBox
                {
                    Tag = save,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0)
                };
                checkBox.Checked += CloudSaveCheckBox_Changed;
                checkBox.Unchecked += CloudSaveCheckBox_Changed;
                Grid.SetColumn(checkBox, 0);
                grid.Children.Add(checkBox);
                colOffset = 1;
            }

            // 存档信息
            var infoPanel = new StackPanel();
            var namePanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };

            // 标签图标
            var tagIcon = new FontIcon
            {
                Glyph = save.IsExitSave ? "\uE7BA" : "\uE74E",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    save.IsExitSave ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.LimeGreen),
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            namePanel.Children.Add(tagIcon);

            namePanel.Children.Add(new TextBlock
            {
                Text = save.Name,
                FontSize = 14,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            });

            infoPanel.Children.Add(namePanel);

            var detailText = new TextBlock
            {
                Text = $"{save.DisplayBackupTime} · {save.DisplaySize}",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Margin = new Microsoft.UI.Xaml.Thickness(20, 2, 0, 0)
            };
            infoPanel.Children.Add(detailText);

            Grid.SetColumn(infoPanel, colOffset);
            grid.Children.Add(infoPanel);

            // 操作按钮
            var btnPanel = new StackPanel
            {
                Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };

            if (!group.IsLocalGameExists && group.CloudGameMetadata != null)
            {
                // 本地游戏不存在时：显示"恢复到本地"按钮（一键导入+恢复）
                var restoreBtn = new Button
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Content = new StackPanel
                    {
                        Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE896", FontSize = 13 },
                            new TextBlock { Text = "恢复到本地", FontSize = 12 }
                        }
                    },
                    Tag = new ImportRestoreContext { Group = group, Save = save }
                };
                restoreBtn.Click += ImportAndRestore_Click;
                btnPanel.Children.Add(restoreBtn);
            }
            else
            {
                // 本地游戏已存在：显示正常的下载按钮
                var downloadBtn = new Button
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Content = new StackPanel
                    {
                        Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE896", FontSize = 13 },
                            new TextBlock { Text = "下载", FontSize = 12 }
                        }
                    },
                    Tag = save
                };
                downloadBtn.Click += DownloadSave_Click;
                btnPanel.Children.Add(downloadBtn);
            }

            // 删除按钮（始终显示）
            var deleteBtn = new Button
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 13,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                },
                Tag = save
            };
            deleteBtn.Click += DeleteCloudSave_Click;
            btnPanel.Children.Add(deleteBtn);

            Grid.SetColumn(btnPanel, _isCloudBatchMode ? 2 : 1);
            grid.Children.Add(btnPanel);

            return grid;
        }

        /// <summary>
        /// 导入游戏配置（仅导入，不恢复存档）
        /// </summary>
        private async void ImportGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CloudSaveGroup group)
            {
                var (success, message) = await ViewModel.ImportGameFromCloudAsync(group);

                var dialog = new ContentDialog
                {
                    Title = success ? "导入成功" : "导入失败",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowWithThemeAsync();

                if (success)
                {
                    // 重新构建 UI 以更新按钮状态
                    BuildSaveGroupsUI();
                }
            }
        }

        /// <summary>
        /// 导入游戏并恢复存档
        /// </summary>
        private async void ImportAndRestore_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ImportRestoreContext ctx)
            {
                DownloadProgressBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                var (success, message) = await ViewModel.ImportAndRestoreAsync(ctx.Group, ctx.Save);
                DownloadProgressBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                var dialog = new ContentDialog
                {
                    Title = success ? "恢复成功" : "恢复失败",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowWithThemeAsync();

                if (success)
                {
                    // 重新构建 UI 以更新按钮状态
                    BuildSaveGroupsUI();
                }
            }
        }

        /// <summary>
        /// 下载云端存档
        /// </summary>
        private async void DownloadSave_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SaveFile save)
            {
                DownloadProgressBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                var (success, message) = await ViewModel.DownloadSaveAsync(save);
                DownloadProgressBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                var dialog = new ContentDialog
                {
                    Title = success ? "下载完成" : "下载失败",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowWithThemeAsync();
            }
        }

        /// <summary>
        /// 删除云端上整个游戏的所有数据
        /// </summary>
        private async void DeleteGameFromCloud_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CloudSaveGroup group)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除整个游戏",
                    Content = $"确定要删除游戏「{group.GameName}」在云端的所有数据吗？\n\n这将删除该游戏的全部存档（{group.Saves.Count} 个）和元数据，此操作不可撤销！",
                    PrimaryButtonText = "全部删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await confirmDialog.ShowWithThemeAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var (success, message) = await ViewModel.DeleteGameFromCloudAsync(group);

                    if (success)
                    {
                        // 重新构建 UI
                        if (ViewModel.SaveGroups.Count == 0)
                        {
                            ShowEmptyState();
                        }
                        else
                        {
                            BuildSaveGroupsUI();
                        }
                    }

                    StatusText.Text = message;
                }
            }
        }

        /// <summary>
        /// 删除云端存档
        /// </summary>
        private async void DeleteCloudSave_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SaveFile save)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除云端存档",
                    Content = $"确定要删除云端存档 \"{save.Name}\"（{save.DisplayBackupTime}）吗？\n此操作不可撤销。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await confirmDialog.ShowWithThemeAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var (success, message) = await ViewModel.DeleteCloudSaveAsync(save);
                    if (success)
                    {
                        // 刷新列表
                        await RefreshCloudSaves();
                    }

                    StatusText.Text = message;
                }
            }
        }

        #region 云端批量删除

        /// <summary>进入云端批量删除模式</summary>
        private void EnterCloudBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _isCloudBatchMode = true;
            _selectedCloudSaves.Clear();
            EnterCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ConfirmCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            CancelCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            CloudBatchDeleteCountText.Text = "删除所选";
            // 重建 UI 以显示 CheckBox
            BuildSaveGroupsUI();
        }

        /// <summary>取消云端批量删除模式</summary>
        private void CancelCloudBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ExitCloudBatchDeleteMode();
        }

        /// <summary>确认批量删除云端存档</summary>
        private async void ConfirmCloudBatchDelete_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_selectedCloudSaves.Count == 0)
            {
                var hintDialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "请先勾选要删除的云端存档",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await hintDialog.ShowWithThemeAsync();
                return;
            }

            var confirmDialog = new ContentDialog
            {
                Title = "确认批量删除云端存档",
                Content = $"确定要删除选中的 {_selectedCloudSaves.Count} 个云端存档吗？\n\n此操作不可撤销。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await confirmDialog.ShowWithThemeAsync();
            if (result == ContentDialogResult.Primary)
            {
                var savesToDelete = _selectedCloudSaves.ToList();
                var (success, message) = await ViewModel.BatchDeleteCloudSavesAsync(savesToDelete);

                StatusText.Text = message;
                ExitCloudBatchDeleteMode();

                // 刷新列表
                await RefreshCloudSaves();
            }
        }

        /// <summary>CheckBox 选中/取消选中时更新已选列表</summary>
        private void CloudSaveCheckBox_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is SaveFile save)
            {
                if (cb.IsChecked == true)
                {
                    if (!_selectedCloudSaves.Contains(save))
                        _selectedCloudSaves.Add(save);
                }
                else
                {
                    _selectedCloudSaves.Remove(save);
                }

                CloudBatchDeleteCountText.Text = _selectedCloudSaves.Count > 0
                    ? $"删除所选 ({_selectedCloudSaves.Count})"
                    : "删除所选";
            }
        }

        /// <summary>退出云端批量删除模式</summary>
        private void ExitCloudBatchDeleteMode()
        {
            _isCloudBatchMode = false;
            _selectedCloudSaves.Clear();
            EnterCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ConfirmCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            CancelCloudBatchDeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            // 重建 UI 以移除 CheckBox
            BuildSaveGroupsUI();
        }

        #endregion

        #region 状态切换

        private void UpdateVisibilityStates()
        {
            if (ViewModel.CloudConfigs.Count == 0)
            {
                NoConfigPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                ContentScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            else
            {
                NoConfigPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        }

        private void ShowLoadingState()
        {
            NoConfigPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ContentScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        private void ShowEmptyState()
        {
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ContentScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        private void ShowContentState()
        {
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ContentScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }

        #endregion
    }

    /// <summary>
    /// 导入恢复上下文（用于按钮 Tag 传递数据）
    /// </summary>
    internal class ImportRestoreContext
    {
        public CloudSaveGroup Group { get; set; } = null!;
        public SaveFile Save { get; set; } = null!;
    }
}
