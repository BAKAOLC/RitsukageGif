using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingWarningDialog : Window
    {
        private readonly CheckBox _doNotAskAgain = new();

        internal ScreenshotScrollingWarningDialog(ScrollCaptureWarningKind kind)
        {
            var fileOnly = kind == ScrollCaptureWarningKind.FileOnly;
            var title = fileOnly ? "ScrollCaptureFileOnlyWarningTitle" : "ScrollCaptureMatchFailureWarningTitle";
            Width = 420;
            CanResize = false;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            Topmost = true;
            TransientWindowShell.Configure(this);
            Bind(TitleProperty, LiveText.Resource(title));
            _doNotAskAgain.Bind(ContentProperty, LiveText.Resource("ScrollCaptureDoNotAskAgain"));
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            };
            var confirm = new Button
            {
                [!ContentProperty] = LiveText.Resource(fileOnly
                    ? "ScrollCaptureContinue"
                    : "SelectionCommandConfirm"),
                IsDefault = true,
            };
            if (fileOnly)
            {
                var stop = new Button { [!ContentProperty] = LiveText.Resource("ScrollCaptureStop"), IsCancel = true };
                stop.Click += (_, _) => Close(false);
                buttons.Children.Add(stop);
            }
            else
            {
                confirm.IsCancel = true;
            }

            confirm.Click += (_, _) => Close(true);
            buttons.Children.Add(confirm);
            Opened += (_, _) => confirm.Focus();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(title),
                        FontWeight = FontWeight.SemiBold,
                        Foreground = AppTheme.Error,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(fileOnly
                            ? "ScrollCaptureFileOnlyWarningMessage"
                            : "ScrollCaptureMatchFailureWarningMessage"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _doNotAskAgain,
                    buttons,
                },
            };
        }

        internal bool DoNotAskAgain => _doNotAskAgain.IsChecked == true;
    }
}
