using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class CaptureSourceLostWindow : Window
    {
        internal CaptureSourceLostWindow(Func<Task> reselect)
        {
            Bind(TitleProperty, LiveText.Resource("WindowSourceLostTitle"));
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            var select = new Button { [!ContentProperty] = LiveText.Resource("WindowSourceReselect") };
            var close = new Button { [!ContentProperty] = LiveText.Resource("Close") };
            select.Click += async (_, _) =>
            {
                Close();
                await reselect();
            };
            close.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new(16), Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("WindowSourceLostStopped"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8, Children = { select, close },
                    },
                },
            };
        }
    }
}
