using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace GameSave.Controls
{
    /// <summary>
    /// 自适应换行面板 - 根据可用空间自动调整列数
    /// 类似 CSS Grid 的 auto-fill + minmax() 效果
    /// 每行的卡片等宽分布，行高取该行最高子元素的高度
    /// </summary>
    public class AdaptiveWrapPanel : Panel
    {
        /// <summary>
        /// 每个子项的最小宽度，当可用空间足够时自动增加列数
        /// </summary>
        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register(nameof(MinItemWidth), typeof(double), typeof(AdaptiveWrapPanel),
                new PropertyMetadata(300.0, OnLayoutPropertyChanged));

        /// <summary>
        /// 列间水平间距
        /// </summary>
        public static readonly DependencyProperty HorizontalSpacingProperty =
            DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(AdaptiveWrapPanel),
                new PropertyMetadata(16.0, OnLayoutPropertyChanged));

        /// <summary>
        /// 行间垂直间距
        /// </summary>
        public static readonly DependencyProperty VerticalSpacingProperty =
            DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(AdaptiveWrapPanel),
                new PropertyMetadata(16.0, OnLayoutPropertyChanged));

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public double HorizontalSpacing
        {
            get => (double)GetValue(HorizontalSpacingProperty);
            set => SetValue(HorizontalSpacingProperty, value);
        }

        public double VerticalSpacing
        {
            get => (double)GetValue(VerticalSpacingProperty);
            set => SetValue(VerticalSpacingProperty, value);
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((AdaptiveWrapPanel)d).InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0)
                return new Size(0, 0);

            double availWidth = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
            int columns = CalculateColumns(availWidth);
            double itemWidth = CalculateItemWidth(availWidth, columns);
            var childAvailableSize = new Size(itemWidth, double.PositiveInfinity);

            // 测量所有子元素
            foreach (var child in Children)
            {
                child.Measure(childAvailableSize);
            }

            // 计算总高度（每行取最高子元素的高度）
            double totalHeight = 0;
            int rowCount = (int)Math.Ceiling((double)Children.Count / columns);

            for (int row = 0; row < rowCount; row++)
            {
                double rowHeight = 0;
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= Children.Count) break;
                    rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
                }
                totalHeight += rowHeight;
                if (row < rowCount - 1)
                    totalHeight += VerticalSpacing;
            }

            return new Size(availWidth, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0)
                return finalSize;

            int columns = CalculateColumns(finalSize.Width);
            double itemWidth = CalculateItemWidth(finalSize.Width, columns);
            int rowCount = (int)Math.Ceiling((double)Children.Count / columns);

            double y = 0;
            for (int row = 0; row < rowCount; row++)
            {
                // 计算该行最大高度
                double rowHeight = 0;
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= Children.Count) break;
                    rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
                }

                // 排列该行子元素
                double x = 0;
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= Children.Count) break;

                    Children[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
                    x += itemWidth + HorizontalSpacing;
                }

                y += rowHeight + VerticalSpacing;
            }

            return finalSize;
        }

        /// <summary>
        /// 根据可用宽度和最小项宽度计算列数
        /// </summary>
        private int CalculateColumns(double availableWidth)
        {
            if (MinItemWidth <= 0) return 1;
            int columns = (int)((availableWidth + HorizontalSpacing) / (MinItemWidth + HorizontalSpacing));
            return Math.Max(1, Math.Min(columns, Children.Count));
        }

        /// <summary>
        /// 根据列数计算每个子项的实际宽度（均分可用空间）
        /// </summary>
        private double CalculateItemWidth(double availableWidth, int columns)
        {
            if (columns <= 1) return availableWidth;
            return (availableWidth - (columns - 1) * HorizontalSpacing) / columns;
        }
    }
}
