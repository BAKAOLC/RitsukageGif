using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedSelectionSpacingDialog : Window
    {
        private readonly CheckBox _doNotAskAgain = new()
        {
            [!ContentProperty] = LiveText.Resource("PinnedNegativeSpacingDoNotAskAgain"),
        };

        internal PinnedSelectionSpacingDialog()
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("PinnedNegativeSpacingTitle");
            var confirm = new Button
            {
                Name = "PinnedNegativeSpacingContinue", IsDefault = true,
                [!ContentProperty] = LiveText.Resource("PinnedNegativeSpacingContinue"),
            };
            var cancel = new Button
            {
                Name = "PinnedNegativeSpacingCancel", IsCancel = true,
                [!ContentProperty] = LiveText.Resource("EditorCancel"),
            };
            confirm.Click += (_, _) => Close(true);
            cancel.Click += (_, _) => Close(false);
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("PinnedNegativeSpacingMessage"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _doNotAskAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6, Children = { confirm, cancel },
                    },
                },
            };
        }

        internal bool DoNotAskAgain => _doNotAskAgain.IsChecked == true;
    }
}
