using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedSelectionSpaceDialog : Window
    {
        internal PinnedSelectionSpaceDialog()
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("PinnedSelectionNotEnoughSpaceTitle");
            var confirm = new Button
            {
                Name = "PinnedSelectionNotEnoughSpaceConfirm", IsDefault = true, IsCancel = true,
                [!ContentProperty] = LiveText.Resource("PinnedSelectionConfirm"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            confirm.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("PinnedSelectionNotEnoughSpaceMessage"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    confirm,
                },
            };
        }
    }
}
