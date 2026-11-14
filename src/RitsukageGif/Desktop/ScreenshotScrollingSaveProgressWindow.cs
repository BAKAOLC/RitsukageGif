using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingSaveProgressWindow : Window
    {
        private readonly Button _cancel = new() { HorizontalAlignment = HorizontalAlignment.Right };
        private readonly CancellationTokenSource _cancellation;
        private bool _finished;

        private ScreenshotScrollingSaveProgressWindow(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
            Width = 360;
            CanResize = false;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Topmost = true;
            TransientWindowShell.Configure(this);
            Bind(TitleProperty, LiveText.Resource("ScreenshotSave"));
            _cancel.Bind(ContentProperty, LiveText.Resource("Cancel"));
            _cancel.Click += (_, _) => CancelSave();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children = { new ProgressBar { IsIndeterminate = true, Height = 8 }, _cancel },
            };
            KeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                CancelSave();
            };
            Closing += (_, args) =>
            {
                if (_finished) return;
                args.Cancel = args.CloseReason != WindowCloseReason.OwnerWindowClosing;
                CancelSave();
            };
        }

        internal static async Task<ScreenshotSavedOutput?> RunAsync(Window owner,
            Func<CancellationToken, Task<ScreenshotSavedOutput>> save, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var dialog = new ScreenshotScrollingSaveProgressWindow(cancellation);
            var shown = dialog.ShowDialog(owner);
            try
            {
                return await save(cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested &&
                                                     !token.IsCancellationRequested)
            {
                return null;
            }
            finally
            {
                dialog._finished = true;
                if (dialog.IsVisible) dialog.Close();
                await shown;
            }
        }

        private void CancelSave()
        {
            if (_cancellation.IsCancellationRequested) return;
            _cancel.IsEnabled = false;
            _cancel.Bind(ContentProperty, LiveText.Resource("Cancelling"));
            _cancellation.Cancel();
        }
    }
}
