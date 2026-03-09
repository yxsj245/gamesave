using GameSave.Models;
using GameSave.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace GameSave.Views
{
    /// <summary>
    /// 云端存档页面
    /// 展示云端存档列表（按游戏分组），支持下载和删除
    /// </summary>
    public partial class CloudPage : Page
    {
        public CloudViewModel ViewModel { get; } = new CloudViewModel();

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

            foreach (var group in ViewModel.SaveGroups)
            {
                // 游戏分组卡片
                var card = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                    Padding = new Microsoft.UI.Xaml.Thickness(20)
                };

                var cardContent = new StackPanel { Spacing = 12 };

                // 游戏名称标题
                var headerPanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
                headerPanel.Children.Add(new FontIcon
                {
                    Glyph = "\uE7FC",
                    FontSize = 18,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                });
                headerPanel.Children.Add(new TextBlock
                {
                    Text = group.GameName,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                });
                headerPanel.Children.Add(new TextBlock
                {
                    Text = group.DisplayCount,
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
                });

                cardContent.Children.Add(headerPanel);

                // 分隔线
                cardContent.Children.Add(new Border
                {
                    Height = 1,
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 4)
                });

                // 存档项列表
                foreach (var save in group.Saves)
                {
                    var saveItem = CreateSaveItemUI(save, group.GameId);
                    cardContent.Children.Add(saveItem);
                }

                card.Child = cardContent;
                groupPanels.Children.Add(card);
            }

            // 用一个手动的方式替换 ItemsRepeater（因为动态数据模板在代码后端更灵活）
            ContentScrollViewer.Content = groupPanels;
        }

        /// <summary>
        /// 创建单个存档项的 UI 元素
        /// </summary>
        private UIElement CreateSaveItemUI(SaveFile save, string gameId)
        {
            var grid = new Grid
            {
                Padding = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 6)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });

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

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // 操作按钮
            var btnPanel = new StackPanel
            {
                Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };

            // 下载按钮
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

            // 删除按钮
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

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);

            return grid;
        }

        /// <summary>
        /// 下载云端存档
        /// </summary>
        private async void DownloadSave_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SaveFile save)
            {
                var (success, message) = await ViewModel.DownloadSaveAsync(save);

                var dialog = new ContentDialog
                {
                    Title = success ? "下载完成" : "下载失败",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
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

                var result = await confirmDialog.ShowAsync();
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
}
