using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class QuickRecordingDialog : Window
    {
        private readonly ComboBox _quality;
        private readonly CheckBox _skipReminder = new();

        internal QuickRecordingDialog(RecordingReviewQuality quality)
        {
            Title = UiText.Get("RecordingQuickMode");
            Width = 460;
            CanResize = false;
            ShowInTaskbar = false;
            Background = AppTheme.Surface;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _quality = new()
            {
                ItemsSource = new[]
                {
                    UiText.Get("RecordingReviewQualityEconomy"), UiText.Get("RecordingReviewQualityStandard"),
                    UiText.Get("RecordingReviewQualityOriginal"),
                },
                SelectedIndex = (int)quality, MinWidth = 180,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _skipReminder.Content = UiText.Get("RecordingQuickDoNotRemind");
            var confirm = new Button { Content = UiText.Get("AnnotationNumberConfirm"), IsDefault = true };
            var cancel = new Button { Content = UiText.Get("Cancel"), IsCancel = true };
            confirm.Click += (_, _) => Close(true);
            cancel.Click += (_, _) => Close(false);
            var exportQuality = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
            exportQuality.Children.Add(new TextBlock
            {
                Text = UiText.Get("RecordingReviewQuality"), VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(_quality, 1);
            exportQuality.Children.Add(_quality);
            Content = new StackPanel
            {
                Spacing = 10, Margin = new(16),
                Children =
                {
                    new TextBlock { Text = UiText.Get("RecordingQuickDescription"), TextWrapping = TextWrapping.Wrap },
                    exportQuality,
                    _skipReminder,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Right, Children = { confirm, cancel },
                    },
                },
            };
        }

        internal RecordingReviewQuality Quality => (RecordingReviewQuality)_quality.SelectedIndex;
        internal bool SkipReminder => _skipReminder.IsChecked == true;
    }
}
