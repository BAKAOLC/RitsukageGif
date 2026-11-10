using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class TableMergeConfirmationDialog : Window
    {
        internal TableMergeConfirmationDialog()
        {
            Width = 400;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("TableMergeCells");
            var cancel = new Button
            {
                Name = "TableMergeCancel", IsDefault = true, IsCancel = true,
                [!ContentProperty] = LiveText.Resource("Cancel"),
            };
            var merge = new Button
            {
                Name = "TableMergeConfirm", [!ContentProperty] = LiveText.Resource("TableMerge"),
            };
            cancel.Click += (_, _) => Close(false);
            merge.Click += (_, _) => Close(true);
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("TableMergeDiscardMessage"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6, Children = { cancel, merge },
                    },
                },
            };
            Opened += (_, _) => cancel.Focus();
        }
    }
}
