using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageBatchProgressWindow : Window
    {
        private readonly TextBlock _label = new();
        private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 6 };
        private bool _complete;

        internal PinnedImageBatchProgressWindow(CancellationTokenSource cancellation)
        {
            Width = 360;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this[!TitleProperty] = LiveText.Resource("PinnedSelectionSave");
            var cancel = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                [!ContentProperty] = LiveText.Resource("Cancel"),
            };
            cancel.Click += (_, _) => cancellation.Cancel();
            Content = new StackPanel { Margin = new(16), Spacing = 12, Children = { _label, _progress, cancel } };
            Closing += (_, _) =>
            {
                if (!_complete) cancellation.Cancel();
            };
        }

        internal void Report(int completed, int total)
        {
            _label.Text = UiText.Format("PinnedSelectionSaveProgress", completed, total);
            _progress.Value = (double)completed / total;
        }

        internal void Finish()
        {
            _complete = true;
            Close();
        }
    }
}
