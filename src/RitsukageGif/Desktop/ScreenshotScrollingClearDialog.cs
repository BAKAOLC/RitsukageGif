using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingClearDialog : Window
    {
        private readonly CheckBox _doNotAskAgain = new();

        internal ScreenshotScrollingClearDialog()
        {
            Width = 420;
            CanResize = false;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            TransientWindowShell.Configure(this);
            Bind(TitleProperty, LiveText.Resource("ScrollCaptureClearTitle"));
            _doNotAskAgain.Bind(ContentProperty, LiveText.Resource("ScrollCaptureDoNotAskAgain"));
            var clear = new Button { [!ContentProperty] = LiveText.Resource("SelectionCommandConfirm") };
            var cancel = new Button
                { [!ContentProperty] = LiveText.Resource("Cancel"), IsCancel = true, IsDefault = true };
            clear.Click += (_, _) => Close(true);
            cancel.Click += (_, _) => Close(false);
            Opened += (_, _) => cancel.Focus();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("ScrollCaptureClearQuestion"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _doNotAskAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Right, Children = { clear, cancel },
                    },
                },
            };
        }

        internal bool DoNotAskAgain => _doNotAskAgain.IsChecked == true;
    }
}
