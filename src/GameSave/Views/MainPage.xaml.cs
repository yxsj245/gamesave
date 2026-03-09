namespace GameSave.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        public MainViewModel ViewModel { get; } = new MainViewModel();

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

        private void GameCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.UIElement element)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.InsertKeyFrame(1f, new System.Numerics.Vector3(1.05f, 1.05f, 1f));
                animation.Duration = TimeSpan.FromMilliseconds(200);

                // C# UWP/WinUI cast standard width/height to center origin
                visual.CenterPoint = new System.Numerics.Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);
                visual.StartAnimation("Scale", animation);
            }
        }

        private void GameCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.UIElement element)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
                animation.Duration = TimeSpan.FromMilliseconds(200);
                visual.CenterPoint = new System.Numerics.Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);
                visual.StartAnimation("Scale", animation);
            }
        }

        private void CloseDetails_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.CloseDetails();
        }

        private void GamesGridView_ItemClick(object sender, Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameSave.Models.Game game)
            {
                ViewModel.SelectedGame = game;
            }
        }
    }
}
