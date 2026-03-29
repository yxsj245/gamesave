using GameSave.Helpers;
using GameSave.Models;
using GameSave.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GameSave.Views
{
    /// <summary>
    /// 主页 - 游戏列表和存档管理
    /// </summary>
    public partial class HomePage : Page
    {
        private Storyboard? _viewModeTransitionStoryboard;

        public HomePage()
        {
            this.InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ApplyViewModeImmediately(ViewModel.HomeGameListViewMode);
            SyncViewModeSwitchSelection();
            UpdateDragReorderState();

            // 监听游戏崩溃检测事件，弹窗提示用户（使用具名方法，确保可取消订阅）
            App.GameService.GameCrashDetected += OnGameCrashDetected;
            // 监听进程启动失败/崩溃事件（多进程场景）
            App.GameService.ProcessLaunchFailed += OnProcessLaunchFailed;
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
            SyncViewModeSwitchSelection();
            UpdateEmptyState();
            UpdateDragReorderState();
        }

        /// <summary>离开页面时取消事件订阅，防止内存泄漏</summary>
        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            StopViewModeTransition();
            App.GameService.GameCrashDetected -= OnGameCrashDetected;
            App.GameService.ProcessLaunchFailed -= OnProcessLaunchFailed;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.Cleanup();
        }

        /// <summary>游戏崩溃检测事件处理（从构造函数的匿名 lambda 提取为具名方法，确保可取消订阅）</summary>
        private async void OnGameCrashDetected(object? sender, string gameName)
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
        }

        /// <summary>进程启动失败/崩溃事件处理（多进程场景下某个进程在5秒内退出）</summary>
        private async void OnProcessLaunchFailed(object? sender, string errorMessage)
        {
            var dispatcherQueue = App.MainWindow?.DispatcherQueue;
            if (dispatcherQueue != null)
            {
                dispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        // 显示主窗口（从托盘还原）
                        App.RestoreMainWindow();
                        await ShowMessageAsync("⚠️ 进程启动失败", errorMessage);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HomePage] 进程启动失败提示弹窗异常: {ex.Message}");
                    }
                });
            }
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
            else if (e.PropertyName == nameof(ViewModel.HomeGameListViewMode))
            {
                SyncViewModeSwitchSelection();
                HideDragIndicator();
                ClearTileDropTargetHighlight();
                AnimateViewModeChange(ViewModel.HomeGameListViewMode);
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

        /// <summary>立即应用视图模式，不播放动画</summary>
        private void ApplyViewModeImmediately(HomeGameListViewMode mode)
        {
            StopViewModeTransition();

            var activeView = GetViewElement(mode);
            var inactiveView = GetViewElement(mode == HomeGameListViewMode.List
                ? HomeGameListViewMode.Tile
                : HomeGameListViewMode.List);

            SetViewState(activeView, true, 1, 0, true, 0);
            SetViewState(inactiveView, false, 0, 0, false, 0);
        }

        /// <summary>执行列表和平铺之间的切换动画</summary>
        private void AnimateViewModeChange(HomeGameListViewMode mode)
        {
            var incomingView = GetViewElement(mode);
            var outgoingView = GetViewElement(mode == HomeGameListViewMode.List
                ? HomeGameListViewMode.Tile
                : HomeGameListViewMode.List);

            if (ReferenceEquals(incomingView, outgoingView))
            {
                ApplyViewModeImmediately(mode);
                return;
            }

            StopViewModeTransition();

            var incomingOffset = mode == HomeGameListViewMode.Tile ? 22d : -22d;
            var outgoingOffset = mode == HomeGameListViewMode.Tile ? -14d : 14d;

            SetViewState(incomingView, true, 0, incomingOffset, false, 1);
            SetViewState(outgoingView, true, 1, 0, false, 0);

            var storyboard = new Storyboard();

            storyboard.Children.Add(CreateOpacityAnimation(incomingView, 0, 1, 240, new CubicEase { EasingMode = EasingMode.EaseOut }));
            storyboard.Children.Add(CreateTranslateAnimation(incomingView, incomingOffset, 0, 280, new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 }));
            storyboard.Children.Add(CreateOpacityAnimation(outgoingView, 1, 0, 170, new CubicEase { EasingMode = EasingMode.EaseIn }));
            storyboard.Children.Add(CreateTranslateAnimation(outgoingView, 0, outgoingOffset, 180, new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 4 }));

            _viewModeTransitionStoryboard = storyboard;
            storyboard.Completed += (_, _) =>
            {
                if (!ReferenceEquals(_viewModeTransitionStoryboard, storyboard))
                    return;

                SetViewState(incomingView, true, 1, 0, true, 0);
                SetViewState(outgoingView, false, 0, 0, false, 0);
                _viewModeTransitionStoryboard = null;
            };
            storyboard.Begin();
        }

        /// <summary>停止视图切换动画</summary>
        private void StopViewModeTransition()
        {
            if (_viewModeTransitionStoryboard == null)
                return;

            _viewModeTransitionStoryboard.Stop();
            _viewModeTransitionStoryboard = null;
        }

        /// <summary>获取指定模式对应的视图元素</summary>
        private FrameworkElement GetViewElement(HomeGameListViewMode mode) =>
            mode == HomeGameListViewMode.Tile ? GamesTileView : GamesList;

        /// <summary>设置视图元素的显示状态</summary>
        private static void SetViewState(
            FrameworkElement element,
            bool isVisible,
            double opacity,
            double translateY,
            bool isHitTestVisible,
            int zIndex)
        {
            element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = opacity;
            element.IsHitTestVisible = isHitTestVisible;
            Canvas.SetZIndex(element, zIndex);

            if (element.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }

            transform.Y = translateY;
        }

        /// <summary>创建透明度动画</summary>
        private static DoubleAnimation CreateOpacityAnimation(
            FrameworkElement target,
            double from,
            double to,
            int durationMs,
            EasingFunctionBase easing)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, "Opacity");
            return animation;
        }

        /// <summary>创建纵向位移动画</summary>
        private static DoubleAnimation CreateTranslateAnimation(
            FrameworkElement target,
            double from,
            double to,
            int durationMs,
            EasingFunctionBase easing)
        {
            if (target.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                target.RenderTransform = transform;
            }

            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, "Y");
            return animation;
        }

        /// <summary>同步顶部视图切换控件与当前展示模式</summary>
        private void SyncViewModeSwitchSelection()
        {
            if (ViewModeSwitch == null)
                return;

            var targetIndex = ViewModel.IsTileViewMode ? 1 : 0;
            if (ViewModeSwitch.SelectedIndex != targetIndex)
            {
                ViewModeSwitch.SelectedIndex = targetIndex;
            }
        }

        /// <summary>切换游戏列表展示模式</summary>
        private async void ViewModeSwitch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModeSwitch.SelectedIndex < 0)
                return;

            var nextMode = ViewModeSwitch.SelectedIndex == 1
                ? HomeGameListViewMode.Tile
                : HomeGameListViewMode.List;

            await ViewModel.SetHomeGameListViewModeAsync(nextMode);
            HideDragIndicator();
            ClearTileDropTargetHighlight();
        }

        /// <summary>搜索框文本变化时实时过滤游戏列表</summary>
        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // 仅处理用户输入引起的文本变更
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.SearchKeyword = sender.Text;
                UpdateEmptyState();
                // 搜索状态下禁用拖拽排序（搜索结果中排序无意义）
                UpdateDragReorderState();
            }
        }

        /// <summary>搜索框提交查询</summary>
        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ViewModel.SearchKeyword = args.QueryText;
            UpdateEmptyState();
            UpdateDragReorderState();
        }

        #endregion

        #region 拖拽排序

        /// <summary>正在拖拽的游戏对象</summary>
        private Game? _draggedGame;

        /// <summary>本次拖拽是否已经发生顺序变更</summary>
        private bool _hasPendingOrderPersist;

        /// <summary>当前高亮的平铺目标项索引</summary>
        private int _tileDropHighlightIndex = -1;

        /// <summary>当前高亮的平铺目标容器</summary>
        private GridViewItem? _tileDropHighlightContainer;

        /// <summary>根据搜索状态切换拖拽排序的启用/禁用</summary>
        private void UpdateDragReorderState()
        {
            var isSearching = !string.IsNullOrEmpty(ViewModel.SearchKeyword?.Trim());
            GamesList.CanDragItems = !isSearching;
            GamesList.AllowDrop = !isSearching;
            GamesTileView.CanDragItems = !isSearching;
            GamesTileView.AllowDrop = !isSearching;

            if (isSearching)
            {
                HideDragIndicator();
                ClearTileDropTargetHighlight();
            }
        }

        /// <summary>拖拽开始：记录被拖拽的游戏</summary>
        private void BeginGameDrag(DragItemsStartingEventArgs e)
        {
            _hasPendingOrderPersist = false;
            HideDragIndicator();
            ClearTileDropTargetHighlight();

            if (e.Items.Count > 0 && e.Items[0] is Game game)
            {
                _draggedGame = game;
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            }
        }

        /// <summary>列表视图开始拖拽</summary>
        private void GamesList_DragItemsStarting(object sender, DragItemsStartingEventArgs e) => BeginGameDrag(e);

        /// <summary>平铺视图开始拖拽</summary>
        private void GamesTileView_DragItemsStarting(object sender, DragItemsStartingEventArgs e) => BeginGameDrag(e);

        /// <summary>
        /// 根据鼠标位置计算目标插入索引，以及指示线应显示的 Y 坐标
        /// </summary>
        private (int targetIndex, double indicatorY) GetListDropTargetInfo(DragEventArgs e)
        {
            var filteredGames = ViewModel.FilteredGames;
            var position = e.GetPosition(GamesList);

            // 遍历列表项，找到目标插入位置
            for (int i = 0; i < filteredGames.Count; i++)
            {
                var container = GamesList.ContainerFromIndex(i) as ListViewItem;
                if (container == null) continue;

                var itemTransform = container.TransformToVisual(GamesList);
                var itemPosition = itemTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var itemHeight = container.ActualHeight;

                // 如果放置点在该项的上半部分，则插入到该项之前
                if (position.Y < itemPosition.Y + itemHeight / 2)
                {
                    return (i, itemPosition.Y);
                }
            }

            // 放到最末尾：获取最后一项的底部位置
            var lastContainer = GamesList.ContainerFromIndex(filteredGames.Count - 1) as ListViewItem;
            if (lastContainer != null)
            {
                var lastTransform = lastContainer.TransformToVisual(GamesList);
                var lastPosition = lastTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
                return (filteredGames.Count, lastPosition.Y + lastContainer.ActualHeight);
            }

            return (filteredGames.Count, 0);
        }

        /// <summary>显示拖拽指示线到指定 Y 坐标</summary>
        private void ShowDragIndicator(double y)
        {
            DragIndicatorLine.Width = GamesList.ActualWidth;
            Canvas.SetLeft(DragIndicatorLine, 0);
            Canvas.SetTop(DragIndicatorLine, y - 1.5); // 居中于目标线
            DragIndicatorLine.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }

        /// <summary>隐藏拖拽指示线</summary>
        private void HideDragIndicator()
        {
            DragIndicatorLine.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        /// <summary>根据指针位置计算平铺视图的目标插入索引</summary>
        private int GetTileDropTargetIndex(DragEventArgs e)
        {
            var filteredGames = ViewModel.FilteredGames;
            if (filteredGames.Count == 0)
                return 0;

            var position = e.GetPosition(GamesTileView);
            var nearestIndex = filteredGames.Count;
            var nearestDistance = double.MaxValue;
            Windows.Foundation.Rect? lastRect = null;

            for (int i = 0; i < filteredGames.Count; i++)
            {
                if (GamesTileView.ContainerFromIndex(i) is not GridViewItem container)
                    continue;

                var transform = container.TransformToVisual(GamesTileView);
                var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var rect = new Windows.Foundation.Rect(topLeft.X, topLeft.Y, container.ActualWidth, container.ActualHeight);

                if (lastRect == null
                    || rect.Bottom > lastRect.Value.Bottom
                    || (Math.Abs(rect.Bottom - lastRect.Value.Bottom) < 0.5 && rect.Right > lastRect.Value.Right))
                {
                    lastRect = rect;
                }

                if (rect.Contains(position))
                {
                    var insertAfter = position.X > rect.X + rect.Width * 0.68
                        || position.Y > rect.Y + rect.Height * 0.72;
                    return Math.Min(i + (insertAfter ? 1 : 0), filteredGames.Count);
                }

                var centerX = rect.X + rect.Width / 2;
                var centerY = rect.Y + rect.Height / 2;
                var distance = Math.Pow(position.X - centerX, 2) + Math.Pow(position.Y - centerY, 2);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            if (lastRect.HasValue
                && position.Y >= lastRect.Value.Y + lastRect.Value.Height * 0.55
                && position.X >= lastRect.Value.X)
            {
                return filteredGames.Count;
            }

            return nearestIndex == filteredGames.Count ? filteredGames.Count : nearestIndex;
        }

        /// <summary>高亮平铺视图中的目标卡片</summary>
        private void HighlightTileDropTarget(int insertIndex)
        {
            var count = ViewModel.FilteredGames.Count;
            if (count == 0)
            {
                ClearTileDropTargetHighlight();
                return;
            }

            var visualIndex = Math.Min(insertIndex, count - 1);
            if (_tileDropHighlightIndex == visualIndex)
                return;

            ClearTileDropTargetHighlight();

            if (GamesTileView.ContainerFromIndex(visualIndex) is GridViewItem container)
            {
                _tileDropHighlightIndex = visualIndex;
                _tileDropHighlightContainer = container;
                container.BorderBrush = CreateAccentBrush();
            }
        }

        /// <summary>清理平铺视图的目标卡片高亮</summary>
        private void ClearTileDropTargetHighlight()
        {
            if (_tileDropHighlightContainer != null)
            {
                _tileDropHighlightContainer.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                _tileDropHighlightContainer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            _tileDropHighlightContainer = null;
            _tileDropHighlightIndex = -1;
        }

        /// <summary>创建主题强调色画刷，用于拖拽高亮</summary>
        private static Microsoft.UI.Xaml.Media.Brush CreateAccentBrush()
        {
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var accentResource)
                && accentResource is Windows.UI.Color accentColor)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(accentColor);
            }

            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);
        }

        /// <summary>在过滤后的集合中移动拖拽项</summary>
        private bool TryMoveDraggedGame(int newIndex)
        {
            if (_draggedGame == null)
                return false;

            var filteredGames = ViewModel.FilteredGames;
            var oldIndex = filteredGames.IndexOf(_draggedGame);
            if (oldIndex < 0)
                return false;

            newIndex = Math.Clamp(newIndex, 0, filteredGames.Count);
            if (newIndex > oldIndex)
            {
                newIndex--;
            }

            if (oldIndex == newIndex)
                return false;

            filteredGames.RemoveAt(oldIndex);
            filteredGames.Insert(newIndex, _draggedGame);
            _hasPendingOrderPersist = true;
            return true;
        }

        /// <summary>将过滤后的顺序同步回主列表并持久化</summary>
        private async Task PersistFilteredGamesOrderAsync()
        {
            var newOrder = ViewModel.FilteredGames.ToList();

            ViewModel.Games.Clear();
            foreach (var game in newOrder)
            {
                ViewModel.Games.Add(game);
            }

            var orderedIds = ViewModel.Games.Select(g => g.Id).ToList();
            await App.ConfigService.ReorderGamesAsync(orderedIds);

            System.Diagnostics.Debug.WriteLine($"[拖拽排序] 已持久化新顺序，共 {orderedIds.Count} 个游戏");
        }

        /// <summary>拖拽经过：设置放置效果并更新指示线位置</summary>
        private void GamesList_DragOver(object sender, DragEventArgs e)
        {
            if (_draggedGame == null) return;

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;

            // 更新指示线位置
            var (_, indicatorY) = GetListDropTargetInfo(e);
            ShowDragIndicator(indicatorY);
            ClearTileDropTargetHighlight();
        }

        /// <summary>放下：计算目标位置并移动游戏</summary>
        private void GamesList_Drop(object sender, DragEventArgs e)
        {
            HideDragIndicator();

            if (_draggedGame == null) return;

            var (newIndex, _) = GetListDropTargetInfo(e);
            TryMoveDraggedGame(newIndex);
        }

        /// <summary>列表视图拖拽离开时隐藏指示线</summary>
        private void GamesList_DragLeave(object sender, DragEventArgs e)
        {
            HideDragIndicator();
        }

        /// <summary>平铺视图拖拽经过时高亮目标卡片</summary>
        private void GamesTileView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggedGame == null) return;

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            HideDragIndicator();

            var newIndex = GetTileDropTargetIndex(e);
            HighlightTileDropTarget(newIndex);
        }

        /// <summary>平铺视图放下时移动到目标位置</summary>
        private void GamesTileView_Drop(object sender, DragEventArgs e)
        {
            if (_draggedGame == null) return;

            var newIndex = GetTileDropTargetIndex(e);
            HighlightTileDropTarget(newIndex);
            TryMoveDraggedGame(newIndex);
        }

        /// <summary>平铺视图拖拽离开时清理目标高亮</summary>
        private void GamesTileView_DragLeave(object sender, DragEventArgs e)
        {
            ClearTileDropTargetHighlight();
        }

        /// <summary>拖拽完成后，将新顺序同步到 Games 并持久化</summary>
        private async void GameCollection_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            HideDragIndicator();
            ClearTileDropTargetHighlight();

            if (_draggedGame == null) return;

            try
            {
                if (_hasPendingOrderPersist)
                {
                    await PersistFilteredGamesOrderAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[拖拽排序] 持久化失败: {ex.Message}");
            }
            finally
            {
                _draggedGame = null;
                _hasPendingOrderPersist = false;
            }
        }

        #endregion

        #region 游戏列表项交互

        private void GamesList_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            HandleGameItemTapped(sender, e);
        }

        private void GamesTileView_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            HandleGameItemTapped(sender, e);
        }

        private void HandleGameItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
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

                    // 检查是否需要进入存档探测模式（有启动进程但无存档路径）
                    if (game.SaveFolderPaths.Count == 0 || game.SaveFolderPaths.All(string.IsNullOrWhiteSpace))
                    {
                        await StartSaveDetectionAsync(game);
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

        /// <summary>
        /// 选择自定义图标文件
        /// </summary>
        private static async Task<string?> PickCustomIconAsync()
        {
            return await ShellDialogHelper.PickFileAsync(
                App.MainWindow,
                [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", "*"]);
        }

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

            // 自定义图标
            var iconPathText = new TextBlock
            {
                Text = "未选择自定义图标，默认优先使用启动程序图标",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            };

            var iconPreviewImage = new Image
            {
                Width = 48,
                Height = 48,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };

            var iconDefaultBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 240, 240, 240)),
                Child = new FontIcon
                {
                    Glyph = "\uE7FC",
                    FontSize = 24,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                }
            };

            var selectedIconPath = string.Empty;
            void UpdateIconPreview(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    iconPreviewImage.Source = null;
                    iconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    iconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    iconPathText.Text = "未选择自定义图标，默认优先使用启动程序图标";
                    ViewModel.NewGameIconPath = null;
                    return;
                }

                var bitmap = IconExtractorHelper.GetIconFromImageFile(path);
                if (bitmap != null)
                {
                    iconPreviewImage.Source = bitmap;
                    iconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    iconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
                else
                {
                    iconPreviewImage.Source = null;
                    iconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    iconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                }

                iconPathText.Text = $"已选择: {System.IO.Path.GetFileName(path)}";
                ViewModel.NewGameIconPath = path;
            }

            var iconPanel = new StackPanel { Spacing = 8, Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0) };
            iconPanel.Children.Add(new TextBlock
            {
                Text = "自定义图标（可选）",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            });

            var iconRow = new Grid();
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            iconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconStack = new Grid { Width = 48, Height = 48 };
            iconStack.Children.Add(iconDefaultBorder);
            iconStack.Children.Add(iconPreviewImage);
            Grid.SetColumn(iconStack, 0);

            var iconInfoPanel = new StackPanel { Spacing = 4, Margin = new Microsoft.UI.Xaml.Thickness(12, 0, 0, 0) };
            iconInfoPanel.Children.Add(new TextBlock
            {
                Text = "选择一张图片作为游戏列表显示图标，若不选择则继续使用启动程序图标。",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            iconInfoPanel.Children.Add(iconPathText);
            Grid.SetColumn(iconInfoPanel, 1);

            var iconButtonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right
            };

            var chooseIconBtn = new Button { Content = "选择图标" };
            chooseIconBtn.Click += async (_, __) =>
            {
                var path = await PickCustomIconAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    selectedIconPath = path;
                    UpdateIconPreview(path);
                }
            };

            var clearIconBtn = new Button { Content = "清除图标" };
            clearIconBtn.Click += (_, __) =>
            {
                selectedIconPath = string.Empty;
                UpdateIconPreview(null);
            };

            iconButtonPanel.Children.Add(chooseIconBtn);
            iconButtonPanel.Children.Add(clearIconBtn);
            Grid.SetColumn(iconButtonPanel, 2);

            iconRow.Children.Add(iconStack);
            iconRow.Children.Add(iconInfoPanel);
            iconRow.Children.Add(iconButtonPanel);
            iconPanel.Children.Add(iconRow);
            panel.Children.Add(iconPanel);

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
                        new TextBlock { Text = "添加路径", FontSize = 12 }
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
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathBox = new TextBox
                {
                    PlaceholderText = "输入路径或点击浏览选择目录/文件",
                    Text = initialPath
                };
                Grid.SetColumn(pathBox, 0);

                // 选择文件夹按钮
                var browseBtn = new Button
                {
                    Content = "文件夹",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseBtn.Click += async (s, args) =>
                {
                    var folderPath = await ShellDialogHelper.PickFolderAsync(App.MainWindow);
                    if (!string.IsNullOrWhiteSpace(folderPath))
                    {
                        // 校验：选择的文件夹不能包含已选择的文件
                        var conflict = ValidateFolderAgainstFiles(folderPath, savePathRows, pathBox);
                        if (conflict != null)
                        {
                            await ShowMessageAsync("路径冲突", conflict);
                            return;
                        }
                        pathBox.Text = PathEnvironmentHelper.ReplaceWithEnvVariables(folderPath);
                    }
                };
                Grid.SetColumn(browseBtn, 1);

                // 选择文件按钮
                var browseFileBtn = new Button
                {
                    Content = "文件",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseFileBtn.Click += async (s, args) =>
                {
                    var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, ["*"]);
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        // 校验：选择的文件不能在已选择的文件夹内
                        var conflict = ValidateFileAgainstFolders(filePath, savePathRows, pathBox);
                        if (conflict != null)
                        {
                            await ShowMessageAsync("路径冲突", conflict);
                            return;
                        }
                        pathBox.Text = PathEnvironmentHelper.ReplaceWithEnvVariables(filePath);
                    }
                };
                Grid.SetColumn(browseFileBtn, 2);

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
                        // 找到删除按钮（第四个子元素，索引3）
                        if (lastRow.Children.Count >= 4)
                        {
                            ((FrameworkElement)lastRow.Children[3]).Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        }
                    }
                };
                Grid.SetColumn(removeBtn, 3);

                rowGrid.Children.Add(pathBox);
                rowGrid.Children.Add(browseBtn);
                rowGrid.Children.Add(browseFileBtn);
                rowGrid.Children.Add(removeBtn);

                savePathsContainer.Children.Add(rowGrid);
                savePathRows.Add((rowGrid, pathBox));

                // 添加新行后，更新所有行的删除按钮可见性
                if (savePathRows.Count > 1)
                {
                    foreach (var (row, _) in savePathRows)
                    {
                        if (row.Children.Count >= 4)
                        {
                            ((FrameworkElement)row.Children[3]).Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        }
                    }
                }
            }

            // 默认添加一行
            AddSavePathRow();

            UpdateIconPreview(null);

            // 加号按钮点击事件
            addPathBtn.Click += (s, args) => AddSavePathRow();

            // ========== 启动进程组 ==========
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
                Text = "启动进程",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

            // 主启动进程（可选）
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "主启动进程（可选）",
                PlaceholderText = "支持 .exe 文件和 .lnk 快捷方式"
            };
            Grid.SetColumn(processPathBox, 0);

            // 启动参数（可选）
            var argsBox = new TextBox
            {
                Header = "主进程启动参数（可选）",
                PlaceholderText = "例如: -windowed -dx12"
            };

            var browseProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseProcessBtn.Click += async (s, args) =>
            {
                var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".exe", ".lnk", "*"]);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    // 如果选择了快捷方式，自动解析目标路径和参数
                    if (ShortcutHelper.IsShortcut(filePath))
                    {
                        var shortcutInfo = ShortcutHelper.ResolveShortcut(filePath);
                        if (shortcutInfo != null && !string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
                        {
                            processPathBox.Text = shortcutInfo.TargetPath;
                            if (!string.IsNullOrWhiteSpace(shortcutInfo.Arguments))
                            {
                                argsBox.Text = shortcutInfo.Arguments;
                            }
                        }
                        else
                        {
                            await ShowMessageAsync("解析失败", "无法解析该快捷方式的目标路径，请手动选择 .exe 文件。");
                        }
                    }
                    else
                    {
                        processPathBox.Text = filePath;
                    }
                }
            };
            Grid.SetColumn(browseProcessBtn, 1);

            processPathPanel.Children.Add(processPathBox);
            processPathPanel.Children.Add(browseProcessBtn);
            panel.Children.Add(processPathPanel);

            panel.Children.Add(argsBox);

            // 第二启动进程（可选）
            var secondaryProcessPathPanel = new Grid();
            secondaryProcessPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secondaryProcessPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var secondaryProcessPathBox = new TextBox
            {
                Header = "第二启动进程（可选，最多2个）",
                PlaceholderText = "支持 .exe 文件和 .lnk 快捷方式"
            };
            Grid.SetColumn(secondaryProcessPathBox, 0);

            var secondaryArgsBox = new TextBox
            {
                Header = "第二进程启动参数（可选）",
                PlaceholderText = "例如: -windowed -dx12"
            };

            var browseSecondaryProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseSecondaryProcessBtn.Click += async (s, args) =>
            {
                var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".exe", ".lnk", "*"]);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    if (ShortcutHelper.IsShortcut(filePath))
                    {
                        var shortcutInfo = ShortcutHelper.ResolveShortcut(filePath);
                        if (shortcutInfo != null && !string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
                        {
                            secondaryProcessPathBox.Text = shortcutInfo.TargetPath;
                            if (!string.IsNullOrWhiteSpace(shortcutInfo.Arguments))
                            {
                                secondaryArgsBox.Text = shortcutInfo.Arguments;
                            }
                        }
                        else
                        {
                            await ShowMessageAsync("解析失败", "无法解析该快捷方式的目标路径，请手动选择 .exe 文件。");
                        }
                    }
                    else
                    {
                        secondaryProcessPathBox.Text = filePath;
                    }
                }
            };
            Grid.SetColumn(browseSecondaryProcessBtn, 1);

            secondaryProcessPathPanel.Children.Add(secondaryProcessPathBox);
            secondaryProcessPathPanel.Children.Add(browseSecondaryProcessBtn);
            panel.Children.Add(secondaryProcessPathPanel);

            panel.Children.Add(secondaryArgsBox);

            panel.Children.Add(new TextBlock
            {
                Text = "提示：多进程模式会按顺序启动，前一个进程启动成功后再启动下一个。所有进程退出后才会执行存档备份。",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

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
                ViewModel.NewGameSecondaryProcessPath = secondaryProcessPathBox.Text;
                ViewModel.NewGameSecondaryProcessArgs = secondaryArgsBox.Text;
                ViewModel.NewGameIconPath = selectedIconPath;

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

                // header 中的游戏名称文本（需要在编辑时同步更新）
                var headerNameText = new TextBlock
                {
                    Text = detected.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    MaxWidth = 280,
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
                };
                headerPanel.Children.Add(headerNameText);

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

                // 游戏名称（可编辑，修改后同步更新 header 显示）
                var nameBox = new TextBox { Header = "游戏名称", Text = detected.Name };
                nameBox.TextChanged += (s, args) =>
                {
                    detected.Name = nameBox.Text;
                    headerNameText.Text = nameBox.Text;
                };
                contentPanel.Children.Add(nameBox);

                // 启动进程（已自动检测 / 未检测到时允许手动选择）
                var hasExePath = !string.IsNullOrEmpty(detected.ExePath);
                var exePathPanel = new Grid();
                exePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                exePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var exeInfo = new TextBox
                {
                    Header = hasExePath ? "启动进程（已自动检测）" : "启动进程（可选，未检测到）",
                    Text = hasExePath ? detected.ExePath! : "",
                    PlaceholderText = hasExePath ? "" : "输入路径或点击浏览选择文件",
                    IsReadOnly = hasExePath,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        hasExePath ? Microsoft.UI.Colors.Gray : Microsoft.UI.Colors.White)
                };
                Grid.SetColumn(exeInfo, 0);

                // 手动输入时同步更新 detected.ExePath
                if (!hasExePath)
                {
                    exeInfo.TextChanged += (s, args) =>
                    {
                        detected.ExePath = exeInfo.Text;
                    };
                }

                exePathPanel.Children.Add(exeInfo);

                // 未检测到时，右侧显示浏览按钮
                if (!hasExePath)
                {
                    var browseExeBtn = new Button
                    {
                        Content = "浏览",
                        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                        Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
                    };
                    browseExeBtn.Click += async (s, args) =>
                    {
                        var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".exe", "*"]);
                        if (!string.IsNullOrWhiteSpace(filePath))
                        {
                            exeInfo.Text = filePath;
                            detected.ExePath = filePath;
                        }
                    };
                    Grid.SetColumn(browseExeBtn, 1);
                    exePathPanel.Children.Add(browseExeBtn);
                }

                contentPanel.Children.Add(exePathPanel);

                // 存档目录（必须用户手动选择）
                var savePathPanel = new Grid();
                savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                savePathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var savePathBox = new TextBox
                {
                    Header = "游戏存档目录（可选，留空可在启动时自动探测）",
                    PlaceholderText = "输入路径或点击浏览选择目录/文件"
                };
                Grid.SetColumn(savePathBox, 0);

                // 手动输入时同步更新 detected.SaveFolderPath
                savePathBox.TextChanged += (s, args) =>
                {
                    detected.SaveFolderPath = savePathBox.Text;
                };

                var browseSaveBtn = new Button
                {
                    Content = "文件夹",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
                };
                browseSaveBtn.Click += async (s, args) =>
                {
                    var folderPath = await ShellDialogHelper.PickFolderAsync(App.MainWindow);
                    if (!string.IsNullOrWhiteSpace(folderPath))
                    {
                        savePathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(folderPath);
                        detected.SaveFolderPath = savePathBox.Text;
                    }
                };
                Grid.SetColumn(browseSaveBtn, 1);

                // 选择文件按钮
                var browseSaveFileBtn = new Button
                {
                    Content = "文件",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseSaveFileBtn.Click += async (s, args) =>
                {
                    var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, ["*"]);
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        savePathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(filePath);
                        detected.SaveFolderPath = savePathBox.Text;
                    }
                };
                Grid.SetColumn(browseSaveFileBtn, 2);

                savePathPanel.Children.Add(savePathBox);
                savePathPanel.Children.Add(browseSaveBtn);
                savePathPanel.Children.Add(browseSaveFileBtn);
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

            // 存档目录为可选项，留空可在启动游戏时通过探测模式自动识别

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

        #region 打开存档目录

        /// <summary>打开备份存档所在的工作目录</summary>
        private async void OpenBackupFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.SelectedGame == null) return;

            try
            {
                var backupDir = App.ConfigService.GetGameWorkDirectory(ViewModel.SelectedGame.Id);
                if (Directory.Exists(backupDir))
                {
                    ShellDialogHelper.OpenPathInExplorer(backupDir);
                }
                else
                {
                    await ShowMessageAsync("提示", "该游戏的备份存档目录尚不存在，请先进行一次备份。");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("打开失败", $"无法打开存档目录: {ex.Message}");
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

            // 检查是否需要进入存档探测模式（有启动进程但无存档路径）
            if (ViewModel.SelectedGame.SaveFolderPaths.Count == 0 || ViewModel.SelectedGame.SaveFolderPaths.All(string.IsNullOrWhiteSpace))
            {
                var game = ViewModel.SelectedGame;
                ViewModel.CloseDetails();
                await StartSaveDetectionAsync(game);
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

        #region 存档目录探测

        /// <summary>
        /// 存档探测核心流程：
        /// 1. 弹窗告知用户探测模式将启动（CPU/内存短暂上升）
        /// 2. 检查管理员权限，非管理员则提权重启
        /// 3. 启动游戏进程获取 PID
        /// 4. 启动 SaveDetectorService 监听
        /// 5. 隐藏主窗口到托盘，打开置顶探测窗口
        /// </summary>
        private async Task StartSaveDetectionAsync(Game game)
        {
            // 1. 二次确认弹窗
            var confirmDialog = new ContentDialog
            {
                Title = "🔍 存档目录探测模式",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"游戏「{game.Name}」未设置存档目录。\n\n" +
                                   "即将启动存档探测模式：\n" +
                                   "• 启动游戏后自动监控文件写入操作\n" +
                                   "• 识别疑似存档目录供您选择\n" +
                                   "• 探测期间 CPU 和内存将会短暂上升",
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "⚠️ 此功能需要管理员权限运行，如当前非管理员将自动提权重启。",
                            FontSize = 12,
                            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                Microsoft.UI.Colors.Orange),
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        }
                    }
                },
                PrimaryButtonText = "开始探测",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await confirmDialog.ShowWithThemeAsync();
            if (result != ContentDialogResult.Primary) return;

            // 2. 检查管理员权限
            if (!AdminHelper.IsRunAsAdmin())
            {
                // 非管理员，提权重启
                var restarted = AdminHelper.RestartAsAdmin();
                if (restarted)
                {
                    // 强制退出当前应用（跳过托盘拦截）
                    App.ForceExit();
                }
                else
                {
                    await ShowMessageAsync("权限不足", "需要管理员权限才能运行存档探测功能。\n请手动以管理员身份运行此应用。");
                }
                return;
            }

            // 3. 启动游戏进程
            System.Diagnostics.Process? process;
            try
            {
                process = App.ProcessMonitorService.LaunchProcess(game.ProcessPath!, game.ProcessArgs);
                if (process == null)
                {
                    await ShowMessageAsync("启动失败", "无法启动游戏进程。");
                    return;
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("启动失败", $"启动游戏进程失败: {ex.Message}");
                return;
            }

            int pid = process.Id;

            // 4. 启动存档探测服务（传入游戏启动进程所在目录）
            string? gameDirectory = null;
            if (!string.IsNullOrEmpty(game.ProcessPath))
            {
                gameDirectory = Path.GetDirectoryName(game.ProcessPath);
            }
            var detector = new SaveDetectorService(gameDirectory);
            try
            {
                detector.Start(pid);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("探测启动失败", $"无法启动 ETW 监控: {ex.Message}\n请确保以管理员身份运行。");
                detector.Dispose();
                return;
            }

            // 5. 隐藏主窗口到托盘
            App.HideToTrayForGame($"{game.Name}（探测模式）");

            // 6. 创建并显示置顶探测窗口
            var detectorWindow = new SaveDetectorWindow(detector, game, pid);

            // 用户确认选择：保存存档目录到游戏配置
            detectorWindow.DirectoriesConfirmed += async (selectedPaths) =>
            {
                try
                {
                    // 更新游戏的存档路径（将绝对路径替换为环境变量形式，如 %LOCALAPPDATA%\...）
                    game.SaveFolderPaths = selectedPaths
                        .Select(p => PathEnvironmentHelper.ReplaceWithEnvVariables(p))
                        .ToList();
                    await App.ConfigService.UpdateGameAsync(game);

                    // 刷新 UI
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                    {
                        ViewModel.ApplySearchFilter();
                    });

                    // 发送托盘通知
                    App.ShowTrayNotification("✅ 存档目录已设置",
                        $"已为「{game.Name}」设置 {selectedPaths.Count} 个存档目录。");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[存档探测] 保存配置失败: {ex.Message}");
                }
                finally
                {
                    detector.Dispose();
                }
            };

            // 用户取消探测
            detectorWindow.DetectionCancelled += () =>
            {
                detector.Dispose();
                App.ShowTrayNotification("ℹ️ 探测已取消",
                    $"已取消「{game.Name}」的存档目录探测。");
            };

            detectorWindow.Activate();
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

            // 检查是否需要进入存档探测模式（有启动进程但无存档路径）
            if (game.SaveFolderPaths.Count == 0 || game.SaveFolderPaths.All(string.IsNullOrWhiteSpace))
            {
                await StartSaveDetectionAsync(game);
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

            // 自定义图标
            var currentIconPath = IconExtractorHelper.ResolveGameIconPath(game.Id, game.IconPath);
            string? selectedIconSourcePath = null;
            var iconWasCleared = false;

            var editIconPathText = new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(currentIconPath)
                    ? $"当前图标: {System.IO.Path.GetFileName(currentIconPath)}"
                    : "未选择自定义图标，默认优先使用启动程序图标",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            };

            var editIconPreviewImage = new Image
            {
                Width = 48,
                Height = 48,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed
            };

            var editIconDefaultBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 240, 240, 240)),
                Child = new FontIcon
                {
                    Glyph = "\uE7FC",
                    FontSize = 24,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                }
            };

            void UpdateEditIconPreview(string? path, bool fromCurrentIcon = false)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    editIconPreviewImage.Source = null;
                    editIconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    editIconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    editIconPathText.Text = "未选择自定义图标，默认优先使用启动程序图标";
                    return;
                }

                var bitmap = IconExtractorHelper.GetIconFromImageFile(path);
                if (bitmap != null)
                {
                    editIconPreviewImage.Source = bitmap;
                    editIconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    editIconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
                else
                {
                    editIconPreviewImage.Source = null;
                    editIconPreviewImage.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    editIconDefaultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                }

                editIconPathText.Text = fromCurrentIcon
                    ? $"当前图标: {System.IO.Path.GetFileName(path)}"
                    : $"已选择: {System.IO.Path.GetFileName(path)}";
            }

            var editIconPanel = new StackPanel { Spacing = 8, Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 0) };
            editIconPanel.Children.Add(new TextBlock
            {
                Text = "自定义图标（可选）",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            });

            var editIconRow = new Grid();
            editIconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            editIconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editIconRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var editIconStack = new Grid { Width = 48, Height = 48 };
            editIconStack.Children.Add(editIconDefaultBorder);
            editIconStack.Children.Add(editIconPreviewImage);
            Grid.SetColumn(editIconStack, 0);

            var editIconInfoPanel = new StackPanel { Spacing = 4, Margin = new Microsoft.UI.Xaml.Thickness(12, 0, 0, 0) };
            editIconInfoPanel.Children.Add(new TextBlock
            {
                Text = "自定义图标会覆盖自动提取的启动程序图标。清除后会恢复为自动图标。",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            editIconInfoPanel.Children.Add(editIconPathText);
            Grid.SetColumn(editIconInfoPanel, 1);

            var editIconButtonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right
            };

            var editChooseIconBtn = new Button { Content = "选择图标" };
            editChooseIconBtn.Click += async (_, __) =>
            {
                var path = await PickCustomIconAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    selectedIconSourcePath = path;
                    iconWasCleared = false;
                    UpdateEditIconPreview(path);
                }
            };

            var editClearIconBtn = new Button { Content = "清除图标" };
            editClearIconBtn.Click += (_, __) =>
            {
                selectedIconSourcePath = null;
                iconWasCleared = true;
                UpdateEditIconPreview(null);
            };

            editIconButtonPanel.Children.Add(editChooseIconBtn);
            editIconButtonPanel.Children.Add(editClearIconBtn);
            Grid.SetColumn(editIconButtonPanel, 2);

            editIconRow.Children.Add(editIconStack);
            editIconRow.Children.Add(editIconInfoPanel);
            editIconRow.Children.Add(editIconButtonPanel);
            editIconPanel.Children.Add(editIconRow);
            panel.Children.Add(editIconPanel);

            if (!string.IsNullOrWhiteSpace(currentIconPath))
            {
                UpdateEditIconPreview(currentIconPath, fromCurrentIcon: true);
            }
            else
            {
                UpdateEditIconPreview(null);
            }

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
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathBox = new TextBox
                {
                    PlaceholderText = "输入路径或点击浏览选择目录/文件",
                    Text = initialPath
                };
                Grid.SetColumn(pathBox, 0);

                // 选择文件夹按钮
                var browseBtn = new Button
                {
                    Content = "文件夹",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseBtn.Click += async (s, args) =>
                {
                    var folderPath = await ShellDialogHelper.PickFolderAsync(App.MainWindow);
                    if (!string.IsNullOrWhiteSpace(folderPath))
                    {
                        // 校验：选择的文件夹不能包含已选择的文件
                        var conflict = ValidateFolderAgainstFiles(folderPath, editSavePathRows, pathBox);
                        if (conflict != null)
                        {
                            await ShowMessageAsync("路径冲突", conflict);
                            return;
                        }
                        pathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(folderPath);
                    }
                };
                Grid.SetColumn(browseBtn, 1);

                // 选择文件按钮
                var browseFileBtn = new Button
                {
                    Content = "文件",
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0)
                };
                browseFileBtn.Click += async (s, args) =>
                {
                    var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, ["*"]);
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        // 校验：选择的文件不能在已选择的文件夹内
                        var conflict = ValidateFileAgainstFolders(filePath, editSavePathRows, pathBox);
                        if (conflict != null)
                        {
                            await ShowMessageAsync("路径冲突", conflict);
                            return;
                        }
                        pathBox.Text = Helpers.PathEnvironmentHelper.ReplaceWithEnvVariables(filePath);
                    }
                };
                Grid.SetColumn(browseFileBtn, 2);

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
                        if (lastRow.Children.Count >= 4)
                        {
                            ((FrameworkElement)lastRow.Children[3]).Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        }
                    }
                };
                Grid.SetColumn(removeBtn, 3);

                rowGrid.Children.Add(pathBox);
                rowGrid.Children.Add(browseBtn);
                rowGrid.Children.Add(browseFileBtn);
                rowGrid.Children.Add(removeBtn);

                editSavePathsContainer.Children.Add(rowGrid);
                editSavePathRows.Add((rowGrid, pathBox));

                if (editSavePathRows.Count > 1)
                {
                    foreach (var (row, _) in editSavePathRows)
                    {
                        if (row.Children.Count >= 4)
                        {
                            ((FrameworkElement)row.Children[3]).Visibility = Microsoft.UI.Xaml.Visibility.Visible;
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

            // ========== 启动进程组 ==========
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
                Text = "启动进程",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 4)
            });

            // 主启动进程
            var processPathPanel = new Grid();
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            processPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var processPathBox = new TextBox
            {
                Header = "主启动进程（可选）",
                PlaceholderText = "支持 .exe 文件和 .lnk 快捷方式",
                Text = game.ProcessPath ?? string.Empty
            };
            Grid.SetColumn(processPathBox, 0);

            var argsBox = new TextBox
            {
                Header = "主进程启动参数（可选）",
                PlaceholderText = "例如: -windowed -dx12",
                Text = game.ProcessArgs ?? string.Empty
            };

            var browseProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseProcessBtn.Click += async (s, args) =>
            {
                var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".exe", ".lnk", "*"]);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    if (ShortcutHelper.IsShortcut(filePath))
                    {
                        var shortcutInfo = ShortcutHelper.ResolveShortcut(filePath);
                        if (shortcutInfo != null && !string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
                        {
                            processPathBox.Text = shortcutInfo.TargetPath;
                            if (!string.IsNullOrWhiteSpace(shortcutInfo.Arguments))
                            {
                                argsBox.Text = shortcutInfo.Arguments;
                            }
                        }
                        else
                        {
                            await ShowMessageAsync("解析失败", "无法解析该快捷方式的目标路径，请手动选择 .exe 文件。");
                        }
                    }
                    else
                    {
                        processPathBox.Text = filePath;
                    }
                }
            };
            Grid.SetColumn(browseProcessBtn, 1);

            processPathPanel.Children.Add(processPathBox);
            processPathPanel.Children.Add(browseProcessBtn);
            panel.Children.Add(processPathPanel);

            panel.Children.Add(argsBox);

            // 第二启动进程
            var editSecondaryProcessPathPanel = new Grid();
            editSecondaryProcessPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editSecondaryProcessPathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var editSecondaryProcessPathBox = new TextBox
            {
                Header = "第二启动进程（可选，最多2个）",
                PlaceholderText = "支持 .exe 文件和 .lnk 快捷方式",
                Text = game.SecondaryProcessPath ?? string.Empty
            };
            Grid.SetColumn(editSecondaryProcessPathBox, 0);

            var editSecondaryArgsBox = new TextBox
            {
                Header = "第二进程启动参数（可选）",
                PlaceholderText = "例如: -windowed -dx12",
                Text = game.SecondaryProcessArgs ?? string.Empty
            };

            var browseEditSecondaryProcessBtn = new Button
            {
                Content = "浏览",
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0)
            };
            browseEditSecondaryProcessBtn.Click += async (s, args) =>
            {
                var filePath = await ShellDialogHelper.PickFileAsync(App.MainWindow, [".exe", ".lnk", "*"]);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    if (ShortcutHelper.IsShortcut(filePath))
                    {
                        var shortcutInfo = ShortcutHelper.ResolveShortcut(filePath);
                        if (shortcutInfo != null && !string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
                        {
                            editSecondaryProcessPathBox.Text = shortcutInfo.TargetPath;
                            if (!string.IsNullOrWhiteSpace(shortcutInfo.Arguments))
                            {
                                editSecondaryArgsBox.Text = shortcutInfo.Arguments;
                            }
                        }
                        else
                        {
                            await ShowMessageAsync("解析失败", "无法解析该快捷方式的目标路径，请手动选择 .exe 文件。");
                        }
                    }
                    else
                    {
                        editSecondaryProcessPathBox.Text = filePath;
                    }
                }
            };
            Grid.SetColumn(browseEditSecondaryProcessBtn, 1);

            editSecondaryProcessPathPanel.Children.Add(editSecondaryProcessPathBox);
            editSecondaryProcessPathPanel.Children.Add(browseEditSecondaryProcessBtn);
            panel.Children.Add(editSecondaryProcessPathPanel);

            panel.Children.Add(editSecondaryArgsBox);

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
                game.SecondaryProcessPath = string.IsNullOrWhiteSpace(editSecondaryProcessPathBox.Text) ? null : editSecondaryProcessPathBox.Text.Trim();
                game.SecondaryProcessArgs = string.IsNullOrWhiteSpace(editSecondaryArgsBox.Text) ? null : editSecondaryArgsBox.Text.Trim();

                if (iconWasCleared)
                {
                    IconExtractorHelper.RemoveCustomIcon(game.Id, game.IconPath);
                    game.IconPath = null;
                }
                else if (!string.IsNullOrWhiteSpace(selectedIconSourcePath))
                {
                    game.IconPath = await IconExtractorHelper.SaveCustomIconAsync(game.Id, selectedIconSourcePath, game.IconPath);
                }

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
                    // 刷新图标缓存（ProcessPath 或自定义图标可能已变更）
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

        #region 存档路径冲突校验

        /// <summary>
        /// 校验选择的文件是否位于已选择的文件夹内
        /// 如果存在冲突，返回冲突提示信息；否则返回 null
        /// </summary>
        /// <param name="filePath">新选择的文件绝对路径</param>
        /// <param name="pathRows">当前所有路径行</param>
        /// <param name="currentPathBox">当前行的文本框（排除自身）</param>
        private static string? ValidateFileAgainstFolders(string filePath, List<(Grid row, TextBox textBox)> pathRows, TextBox currentPathBox)
        {
            var normalizedFile = Path.GetFullPath(filePath).TrimEnd('\\', '/');

            foreach (var (_, textBox) in pathRows)
            {
                // 跳过当前行自身
                if (ReferenceEquals(textBox, currentPathBox))
                    continue;

                var existingPath = textBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(existingPath))
                    continue;

                // 展开环境变量后再比较
                var expandedPath = PathEnvironmentHelper.ExpandEnvVariables(existingPath);
                var normalizedExisting = Path.GetFullPath(expandedPath).TrimEnd('\\', '/');

                // 检查 existingPath 是否是一个目录（通过 Path.HasExtension 和 Directory.Exists 综合判断）
                bool isExistingDir = Directory.Exists(expandedPath) ||
                    (!File.Exists(expandedPath) && !Path.HasExtension(expandedPath));

                if (isExistingDir)
                {
                    // 检查文件是否在该目录下
                    var dirWithSep = normalizedExisting + Path.DirectorySeparatorChar;
                    if (normalizedFile.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"选择的文件位于已添加的文件夹内，会导致备份冲突：\n\n" +
                               $"文件: {filePath}\n" +
                               $"文件夹: {existingPath}\n\n" +
                               $"请选择该文件夹外的文件，或移除已添加的文件夹。";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 校验选择的文件夹是否包含已选择的文件
        /// 如果存在冲突，返回冲突提示信息；否则返回 null
        /// </summary>
        /// <param name="folderPath">新选择的文件夹绝对路径</param>
        /// <param name="pathRows">当前所有路径行</param>
        /// <param name="currentPathBox">当前行的文本框（排除自身）</param>
        private static string? ValidateFolderAgainstFiles(string folderPath, List<(Grid row, TextBox textBox)> pathRows, TextBox currentPathBox)
        {
            var normalizedFolder = Path.GetFullPath(folderPath).TrimEnd('\\', '/');
            var folderWithSep = normalizedFolder + Path.DirectorySeparatorChar;

            foreach (var (_, textBox) in pathRows)
            {
                // 跳过当前行自身
                if (ReferenceEquals(textBox, currentPathBox))
                    continue;

                var existingPath = textBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(existingPath))
                    continue;

                // 展开环境变量后再比较
                var expandedPath = PathEnvironmentHelper.ExpandEnvVariables(existingPath);
                var normalizedExisting = Path.GetFullPath(expandedPath).TrimEnd('\\', '/');

                // 检查 existingPath 是否是一个文件（通过 File.Exists 或 Path.HasExtension 判断）
                bool isExistingFile = (File.Exists(expandedPath) && !Directory.Exists(expandedPath)) ||
                    (!Directory.Exists(expandedPath) && Path.HasExtension(expandedPath));

                if (isExistingFile)
                {
                    // 检查该文件是否在新选择的文件夹下
                    if (normalizedExisting.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"选择的文件夹包含已添加的文件，会导致备份冲突：\n\n" +
                               $"文件夹: {folderPath}\n" +
                               $"文件: {existingPath}\n\n" +
                               $"请选择不包含已添加文件的文件夹，或先移除已添加的文件。";
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
