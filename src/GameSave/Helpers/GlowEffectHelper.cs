using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GameSave.Helpers
{
    /// <summary>
    /// 全局光晕效果附加属性帮助类。
    /// 在任何 FrameworkElement 上设置 helpers:GlowEffectHelper.IsEnabled="True" 即可启用鼠标跟随光晕效果。
    /// 仅在深色模式下显示光晕。
    /// 
    /// 用法示例：
    /// <code>
    ///   xmlns:helpers="using:GameSave.Helpers"
    ///   
    ///   &lt;Border helpers:GlowEffectHelper.IsEnabled="True"&gt;
    ///       &lt;!-- 内容 --&gt;
    ///   &lt;/Border&gt;
    /// </code>
    /// </summary>
    public static class GlowEffectHelper
    {
        // ========== 附加属性：IsEnabled ==========
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(GlowEffectHelper),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) =>
            (bool)obj.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject obj, bool value) =>
            obj.SetValue(IsEnabledProperty, value);

        // ========== 内部附加属性：保存光晕矩形引用 ==========
        private static readonly DependencyProperty GlowRectProperty =
            DependencyProperty.RegisterAttached(
                "GlowRect",
                typeof(Rectangle),
                typeof(GlowEffectHelper),
                new PropertyMetadata(null));

        private static Rectangle? GetGlowRect(DependencyObject obj) =>
            obj.GetValue(GlowRectProperty) as Rectangle;

        private static void SetGlowRect(DependencyObject obj, Rectangle? value) =>
            obj.SetValue(GlowRectProperty, value);

        // ========== 内部附加属性：保存光晕画刷引用 ==========
        private static readonly DependencyProperty GlowBrushProperty =
            DependencyProperty.RegisterAttached(
                "GlowBrush",
                typeof(RadialGradientBrush),
                typeof(GlowEffectHelper),
                new PropertyMetadata(null));

        private static RadialGradientBrush? GetGlowBrush(DependencyObject obj) =>
            obj.GetValue(GlowBrushProperty) as RadialGradientBrush;

        private static void SetGlowBrush(DependencyObject obj, RadialGradientBrush? value) =>
            obj.SetValue(GlowBrushProperty, value);

        /// <summary>
        /// IsEnabled 属性变更回调：挂载或卸载光晕效果
        /// </summary>
        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                // 在元素加载完成后注入光晕层
                if (element.IsLoaded)
                {
                    AttachGlow(element);
                }
                else
                {
                    // 延迟到 Loaded 事件
                    element.Loaded += Element_Loaded;
                }
            }
            else
            {
                DetachGlow(element);
                element.Loaded -= Element_Loaded;
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                element.Loaded -= Element_Loaded;
                AttachGlow(element);
            }
        }

        /// <summary>
        /// 将光晕矩形注入到元素的可视树中
        /// </summary>
        private static void AttachGlow(FrameworkElement element)
        {
            // 如果已经附加过，跳过
            if (GetGlowRect(element) != null)
                return;

            // 创建径向渐变画刷
            var glowBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 1.5
            };
            glowBrush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0x20, 0xFF, 0xFF, 0xFF), Offset = 0 });
            glowBrush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 1 });

            // 创建光晕矩形
            var glowRect = new Rectangle
            {
                Opacity = 0,
                IsHitTestVisible = false,
                Fill = glowBrush
            };

            // 尝试将光晕矩形注入到元素的内容中
            if (element is Border border)
            {
                // Border 只能有一个 Child，需要包装
                var existingChild = border.Child;
                border.Child = null;

                var grid = new Grid();
                grid.Children.Add(glowRect);
                if (existingChild != null)
                {
                    grid.Children.Add(existingChild);
                }
                border.Child = grid;
            }
            else if (element is Panel panel)
            {
                // Panel 类型（Grid、StackPanel 等），在第一个位置插入
                panel.Children.Insert(0, glowRect);
            }
            else
            {
                // 其他类型不支持，直接返回
                System.Diagnostics.Debug.WriteLine($"[GlowEffectHelper] 不支持的元素类型: {element.GetType().Name}");
                return;
            }

            // 保存引用
            SetGlowRect(element, glowRect);
            SetGlowBrush(element, glowBrush);

            // 挂载事件处理器
            element.PointerEntered += Element_PointerEntered;
            element.PointerExited += Element_PointerExited;
            element.PointerMoved += Element_PointerMoved;
        }

        /// <summary>
        /// 卸载光晕效果
        /// </summary>
        private static void DetachGlow(FrameworkElement element)
        {
            element.PointerEntered -= Element_PointerEntered;
            element.PointerExited -= Element_PointerExited;
            element.PointerMoved -= Element_PointerMoved;

            var glowRect = GetGlowRect(element);
            if (glowRect != null)
            {
                // 从可视树中移除光晕矩形
                if (element is Border border && border.Child is Grid grid)
                {
                    grid.Children.Remove(glowRect);
                    // 如果 Grid 只剩一个子元素，还原为原始结构
                    if (grid.Children.Count == 1)
                    {
                        var remaining = grid.Children[0];
                        grid.Children.Clear();
                        border.Child = remaining as UIElement;
                    }
                }
                else if (element is Panel panel)
                {
                    panel.Children.Remove(glowRect);
                }
            }

            SetGlowRect(element, null);
            SetGlowBrush(element, null);
        }

        /// <summary>
        /// 判断当前是否为深色主题
        /// </summary>
        private static bool IsDarkTheme(FrameworkElement element)
        {
            return element.ActualTheme == ElementTheme.Dark;
        }

        // ========== 事件处理器 ==========

        private static void Element_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                // 仅在深色模式下显示光晕
                if (!IsDarkTheme(element))
                    return;

                var glowRect = GetGlowRect(element);
                if (glowRect != null)
                {
                    glowRect.Opacity = 1;
                }
            }
        }

        private static void Element_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var glowRect = GetGlowRect(element);
                if (glowRect != null)
                {
                    glowRect.Opacity = 0;
                }
            }
        }

        private static void Element_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                // 仅在深色模式下更新光晕位置
                if (!IsDarkTheme(element))
                    return;

                var glowRect = GetGlowRect(element);
                var glowBrush = GetGlowBrush(element);

                if (glowRect != null && glowBrush != null)
                {
                    var pointerPosition = e.GetCurrentPoint(element).Position;
                    // 根据元素尺寸计算相对位置
                    double xRelative = pointerPosition.X / element.ActualWidth;
                    double yRelative = pointerPosition.Y / element.ActualHeight;

                    glowBrush.Center = new Point(xRelative, yRelative);
                    glowBrush.GradientOrigin = new Point(xRelative, yRelative);
                }
            }
        }
    }
}
