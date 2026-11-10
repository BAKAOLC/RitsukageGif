using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingToolbarCaptureDialog : Window
    {
        private readonly CheckBox _remember = new();

        internal RecordingToolbarCaptureDialog(bool overlapKnown)
        {
            Title = UiText.Get("RecordingToolbarCaptureTitle");
            Width = 480;
            CanResize = false;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _remember.Content = UiText.Get("RecordingToolbarRemember");
            var hide = new Button { Content = UiText.Get("RecordingToolbarHide") };
            var show = new Button { Content = UiText.Get("RecordingToolbarShow") };
            hide.Click += (_, _) => Close(true);
            show.Click += (_, _) => Close(false);
            Content = new StackPanel
            {
                Spacing = 12, Margin = new(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Get(overlapKnown
                            ? "RecordingToolbarOverlapPrompt"
                            : "RecordingToolbarCapturePrompt"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _remember,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Right, Children = { hide, show },
                    },
                },
            };
        }

        internal bool RememberChoice => _remember.IsChecked == true;
    }
}
