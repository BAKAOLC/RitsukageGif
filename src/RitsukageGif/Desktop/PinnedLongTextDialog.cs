using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedLongTextDialog : Window
    {
        private readonly CheckBox _doNotAskAgain = new()
        {
            Name = "PinnedLongTextDoNotAskAgain",
            [!ContentProperty] = LiveText.Resource("PinnedNegativeSpacingDoNotAskAgain"),
        };

        private bool _accepted;

        internal PinnedLongTextDialog(int length)
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Title = UiText.Get("PinnedLongTextTitle");
            var pin = new Button
            {
                Name = "PinnedLongTextPin", IsDefault = true,
                [!ContentProperty] = LiveText.Resource("PinnedLongTextPin"),
            };
            var cancel = new Button
            {
                Name = "PinnedLongTextCancel", IsCancel = true,
                [!ContentProperty] = LiveText.Resource("EditorCancel"),
            };
            pin.Click += (_, _) =>
            {
                _accepted = true;
                Close();
            };
            cancel.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                        { Text = UiText.Format("PinnedLongTextQuestion", length), TextWrapping = TextWrapping.Wrap },
                    _doNotAskAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6, Children = { pin, cancel },
                    },
                },
            };
        }

        internal bool DoNotAskAgain => _doNotAskAgain.IsChecked == true;

        internal async Task<bool> AskAsync(Window? owner, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Closed += (_, _) => completion.TrySetResult();
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(Close));
            if (owner is { IsVisible: true })
            {
                await ShowDialog(owner);
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Show();
                await completion.Task;
            }

            token.ThrowIfCancellationRequested();
            return _accepted;
        }
    }
}
