using GameSave.Services;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;

namespace GameSave.Views;

/// <summary>
/// 存档目录探测结果窗口
/// 置顶显示，展示 SaveDetectorService 捕获到的疑似存档目录
/// 分三个区块展示：用户目录、游戏安装目录、其他目录
/// </summary>
public sealed partial class SaveDetectorWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const int SM_CXSCREEN = 0; // 屏幕宽度

    private readonly SaveDetectorService _detector;
    private readonly Models.Game _game;
    private readonly int _pid;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private int _currentWidth = 520;
    private const int MinWidth = 520;
    private const int WindowHeight = 650;

    // 每个目录项的 UI 状态（按区域分组管理）
    private readonly List<DirectoryItemState> _userProfileItems = new();
    private readonly List<DirectoryItemState> _gameDirectoryItems = new();
    private readonly List<DirectoryItemState> _otherItems = new();

    /// <summary>
    /// 用户确认选择后触发，参数为选中的目录路径列表
    /// </summary>
    public event Action<List<string>>? DirectoriesConfirmed;

    /// <summary>
    /// 用户取消探测时触发
    /// </summary>
    public event Action? DetectionCancelled;

    /// <summary>
    /// 目录项 UI 状态
    /// </summary>
    private class DirectoryItemState
    {
        public string Path { get; set; } = string.Empty;
        public string SourceZone { get; set; } = SaveDetectorService.ZoneOther;
        public CheckBox CheckBox { get; set; } = null!;
        public StackPanel FilesPanel { get; set; } = null!;
        public StackPanel Panel { get; set; } = null!;
        public TextBlock WriteCountText { get; set; } = null!;
        public TextBlock ScoreText { get; set; } = null!;
        public long CurrentScore { get; set; }
        public bool IsExpanded { get; set; }
    }

    /// <summary>
    /// 获取所有区域的目录项合集
    /// </summary>
    private IEnumerable<DirectoryItemState> AllItems =>
        _userProfileItems.Concat(_gameDirectoryItems).Concat(_otherItems);

    public SaveDetectorWindow(SaveDetectorService detector, Models.Game game, int pid)
    {
        this.InitializeComponent();

        _detector = detector;
        _game = game;
        _pid = pid;

        // 设置窗口大小
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWidth, WindowHeight));

        // 设置窗口图标
        _appWindow.SetIcon("Assets/app.ico");

        // 设置置顶
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

        // 应用主题
        var themeMode = App.ConfigService.ThemeMode;
        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = themeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        // 如果有游戏目录，设置游戏目录区块标题
        if (!string.IsNullOrEmpty(detector.GameDirectory))
        {
            GameDirectoryTitle.Text = $"🎮 游戏安装目录（{detector.GameDirectory}）";
        }

        // 订阅探测事件
        _detector.DirectoryDiscovered += OnDirectoryDiscovered;
        _detector.StatsUpdated += OnStatsUpdated;

        // 加载已有结果
        LoadExistingResults();
    }

    /// <summary>
    /// 加载已有的探测结果（如果探测已运行一段时间）
    /// </summary>
    private void LoadExistingResults()
    {
        var results = _detector.GetResults();
        foreach (var dir in results)
        {
            AddDirectoryItem(dir);
        }
        UpdateEmptyHint();
    }

    /// <summary>
    /// 新目录被发现时的回调
    /// </summary>
    private void OnDirectoryDiscovered(DetectedDirectory dir)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            // 检查是否已存在
            if (AllItems.Any(d => d.Path.Equals(dir.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            AddDirectoryItem(dir);
            UpdateEmptyHint();
            SortDirectoryList(dir.SourceZone);
        });
    }

    /// <summary>
    /// 统计信息更新时的回调（刷新得分和写入次数）
    /// </summary>
    private void OnStatsUpdated()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            RefreshStats();
        });
    }

    /// <summary>
    /// 刷新所有目录项的统计信息（得分、写入次数）
    /// </summary>
    private void RefreshStats()
    {
        var results = _detector.GetResults();
        // 记录哪些区域需要重新排序
        var zonesToSort = new HashSet<string>();

        foreach (var result in results)
        {
            var item = AllItems.FirstOrDefault(d =>
                d.Path.Equals(result.Path, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.WriteCountText.Text = $"写入: {result.WriteCount} 次";
                item.ScoreText.Text = $"得分: {result.Score}";

                // 如果得分变化了，需要重排该区域
                if (item.CurrentScore != result.Score)
                {
                    item.CurrentScore = result.Score;
                    zonesToSort.Add(item.SourceZone);
                }

                // 更新文件列表
                UpdateFilesPanel(item, result.Files);
            }
        }

        // 只重排得分变化的区域
        foreach (var zone in zonesToSort)
        {
            SortDirectoryList(zone);
        }
    }

    /// <summary>
    /// 更新目录项的文件列表面板
    /// </summary>
    private static void UpdateFilesPanel(DirectoryItemState item, List<string> files)
    {
        item.FilesPanel.Children.Clear();
        foreach (var file in files)
        {
            item.FilesPanel.Children.Add(new TextBlock
            {
                Text = $"  📄 {file}",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                Margin = new Thickness(24, 1, 0, 1)
            });
        }
    }

    /// <summary>
    /// 根据 SourceZone 获取对应的列表和容器
    /// </summary>
    private (List<DirectoryItemState> items, StackPanel list, StackPanel section) GetZoneContainers(string sourceZone)
    {
        return sourceZone switch
        {
            SaveDetectorService.ZoneUserProfile => (_userProfileItems, UserProfileList, UserProfileSection),
            SaveDetectorService.ZoneGameDirectory => (_gameDirectoryItems, GameDirectoryList, GameDirectorySection),
            _ => (_otherItems, OtherList, OtherSection)
        };
    }

    /// <summary>
    /// 添加一个目录项到对应区块的列表
    /// </summary>
    private void AddDirectoryItem(DetectedDirectory dir)
    {
        var (items, list, section) = GetZoneContainers(dir.SourceZone);

        var itemPanel = new StackPanel
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(6),
        };

        // 第一行：勾选框 + 展开按钮 + 路径
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // CheckBox
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 展开按钮
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 路径

        var checkBox = new CheckBox
        {
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        checkBox.Checked += (_, _) => UpdateSelectedCount();
        checkBox.Unchecked += (_, _) => UpdateSelectedCount();
        Grid.SetColumn(checkBox, 0);

        // 展开/收缩按钮
        var expandButton = new Button
        {
            Content = "▶",
            FontSize = 10,
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent)
        };
        Grid.SetColumn(expandButton, 1);

        // 路径和统计信息
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var pathText = new TextBlock
        {
            Text = dir.Path,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var writeCountText = new TextBlock
        {
            Text = $"写入: {dir.WriteCount} 次",
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray)
        };
        var scoreText = new TextBlock
        {
            Text = $"得分: {dir.Score}",
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                dir.Score >= 20 ? Microsoft.UI.Colors.OrangeRed :
                dir.Score >= 5 ? Microsoft.UI.Colors.Orange :
                Microsoft.UI.Colors.Gray)
        };
        var filesCountText = new TextBlock
        {
            Text = $"{dir.Files.Count} 个文件",
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray)
        };
        statsPanel.Children.Add(writeCountText);
        statsPanel.Children.Add(scoreText);
        statsPanel.Children.Add(filesCountText);

        infoPanel.Children.Add(pathText);
        infoPanel.Children.Add(statsPanel);
        Grid.SetColumn(infoPanel, 2);

        headerGrid.Children.Add(checkBox);
        headerGrid.Children.Add(expandButton);
        headerGrid.Children.Add(infoPanel);

        // 文件列表面板（默认收缩）
        var filesPanel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(48, 4, 0, 0)
        };

        // 填充文件列表
        foreach (var file in dir.Files)
        {
            filesPanel.Children.Add(new TextBlock
            {
                Text = $"📄 {file}",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                Margin = new Thickness(0, 1, 0, 1)
            });
        }

        // 记录状态
        var itemState = new DirectoryItemState
        {
            Path = dir.Path,
            SourceZone = dir.SourceZone,
            CheckBox = checkBox,
            FilesPanel = filesPanel,
            Panel = itemPanel,
            WriteCountText = writeCountText,
            ScoreText = scoreText,
            CurrentScore = dir.Score,
            IsExpanded = false
        };
        items.Add(itemState);

        // 展开按钮点击事件
        expandButton.Click += (_, _) =>
        {
            itemState.IsExpanded = !itemState.IsExpanded;
            filesPanel.Visibility = itemState.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (expandButton.Content is string)
            {
                expandButton.Content = itemState.IsExpanded ? "▼" : "▶";
            }
        };

        itemPanel.Children.Add(headerGrid);
        itemPanel.Children.Add(filesPanel);

        list.Children.Add(itemPanel);

        // 显示该区块并更新计数
        section.Visibility = Visibility.Visible;
        UpdateZoneCount(dir.SourceZone);

        // 根据路径长度自动调整窗口宽度
        AdjustWindowWidth(dir.Path);
    }

    /// <summary>
    /// 根据最长路径自动调整窗口宽度，最大不超过屏幕宽度
    /// </summary>
    private void AdjustWindowWidth(string newPath)
    {
        if (_appWindow == null) return;

        // 估算路径文本需要的宽度（每个字符约 7.5 像素 + 左右边距 + CheckBox + 展开按钮）
        int estimatedWidth = (int)(newPath.Length * 7.5) + 120;

        if (estimatedWidth <= _currentWidth) return;

        // 获取屏幕宽度（物理像素）
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);

        // 考虑 DPI 缩放
        uint dpi = GetDpiForWindow(_hwnd);
        double scale = dpi / 96.0;
        int maxWidth = (int)(screenWidth / scale);

        int newWidth = Math.Min(estimatedWidth, maxWidth - 50); // 留 50px 边距
        newWidth = Math.Max(newWidth, MinWidth);

        if (newWidth > _currentWidth)
        {
            _currentWidth = newWidth;
            _appWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, WindowHeight));
        }
    }

    /// <summary>
    /// 按得分对指定区域的列表重新排序（将高得分项排前面）
    /// </summary>
    private void SortDirectoryList(string? zone = null)
    {
        // 获取最新得分
        var results = _detector.GetResults();

        // 更新所有项的得分
        foreach (var result in results)
        {
            var item = AllItems.FirstOrDefault(d =>
                d.Path.Equals(result.Path, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                item.CurrentScore = result.Score;
        }

        // 对指定区域排序，如果未指定则全部排序
        if (zone == null || zone == SaveDetectorService.ZoneUserProfile)
            SortZoneList(_userProfileItems, UserProfileList);
        if (zone == null || zone == SaveDetectorService.ZoneGameDirectory)
            SortZoneList(_gameDirectoryItems, GameDirectoryList);
        if (zone == null || zone == SaveDetectorService.ZoneOther)
            SortZoneList(_otherItems, OtherList);
    }

    /// <summary>
    /// 对单个区域的列表进行排序
    /// </summary>
    private static void SortZoneList(List<DirectoryItemState> items, StackPanel listPanel)
    {
        if (items.Count <= 1) return;

        var sorted = items.OrderByDescending(d => d.CurrentScore).ToList();

        listPanel.Children.Clear();
        foreach (var item in sorted)
        {
            listPanel.Children.Add(item.Panel);
        }
    }

    /// <summary>
    /// 更新空状态提示可见性（所有区域都没有目录时显示）
    /// </summary>
    private void UpdateEmptyHint()
    {
        bool hasAny = _userProfileItems.Count > 0 || _gameDirectoryItems.Count > 0 || _otherItems.Count > 0;
        EmptyHint.Visibility = hasAny
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 更新底部已选数量文本（跨区域统一计数）
    /// </summary>
    private void UpdateSelectedCount()
    {
        int count = AllItems.Count(d => d.CheckBox.IsChecked == true);
        SelectedCountText.Text = count > 0
            ? $"已选择 {count} 个目录"
            : "未选择任何目录";
        ConfirmButton.IsEnabled = count > 0;
    }

    /// <summary>
    /// 确认选择按钮点击
    /// </summary>
    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPaths = AllItems
            .Where(d => d.CheckBox.IsChecked == true)
            .Select(d => d.Path)
            .ToList();

        if (selectedPaths.Count == 0) return;

        // 停止探测
        _detector.DirectoryDiscovered -= OnDirectoryDiscovered;
        _detector.StatsUpdated -= OnStatsUpdated;
        _detector.Stop();

        // 触发确认事件（保存存档目录）
        DirectoriesConfirmed?.Invoke(selectedPaths);

        // 弹窗询问是否立即退出游戏
        var exitDialog = new ContentDialog
        {
            Title = "✅ 存档目录已设置",
            Content = $"已选择 {selectedPaths.Count} 个存档目录。\n是否立即退出游戏进程？",
            PrimaryButtonText = "立即退出游戏",
            SecondaryButtonText = "稍后退出",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await exitDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 立即结束游戏进程
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(_pid);
                proc?.Kill();
            }
            catch { /* 进程可能已退出 */ }
        }

        // 恢复主窗口
        App.RestoreMainWindow();

        // 关闭窗口
        this.Close();
    }

    /// <summary>
    /// 取消按钮点击
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // 停止探测
        _detector.DirectoryDiscovered -= OnDirectoryDiscovered;
        _detector.StatsUpdated -= OnStatsUpdated;
        _detector.Stop();

        // 触发取消事件
        DetectionCancelled?.Invoke();

        // 恢复主窗口
        App.RestoreMainWindow();

        // 关闭窗口
        this.Close();
    }

    #region 区块折叠/展开

    // 区块展开状态
    private bool _userProfileExpanded = false;
    private bool _gameDirectoryExpanded = false;
    private bool _otherExpanded = false;

    private void UserProfileHeader_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _userProfileExpanded = !_userProfileExpanded;
        ToggleZone(_userProfileExpanded, UserProfileList, UserProfileArrow);
    }

    private void GameDirectoryHeader_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _gameDirectoryExpanded = !_gameDirectoryExpanded;
        ToggleZone(_gameDirectoryExpanded, GameDirectoryList, GameDirectoryArrow);
    }

    private void OtherHeader_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _otherExpanded = !_otherExpanded;
        ToggleZone(_otherExpanded, OtherList, OtherArrow);
    }

    /// <summary>
    /// 切换区块的展开/折叠状态
    /// </summary>
    private static void ToggleZone(bool expanded, StackPanel list, TextBlock arrow)
    {
        list.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        arrow.Text = expanded ? "▼" : "▶";
    }

    /// <summary>
    /// 更新区块标题中的条目计数
    /// </summary>
    private void UpdateZoneCount(string sourceZone)
    {
        switch (sourceZone)
        {
            case SaveDetectorService.ZoneUserProfile:
                UserProfileCount.Text = $"({_userProfileItems.Count})";
                break;
            case SaveDetectorService.ZoneGameDirectory:
                GameDirectoryCount.Text = $"({_gameDirectoryItems.Count})";
                break;
            case SaveDetectorService.ZoneOther:
                OtherCount.Text = $"({_otherItems.Count})";
                break;
        }
    }

    #endregion
}
