using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private static readonly IBrush ActiveFrameBrush = new ImmutableSolidColorBrush(0xffe53e31);
        private static readonly IBrush WaitingFrameBrush = new ImmutableSolidColorBrush(0xfffbc31f);
        private RecordingCountdownWindow? _countdownWindow;

        private void UpdateCountdownWindow()
        {
            if (_controller.CountdownRemaining is not { } remaining || !_hasShown)
            {
                CloseCountdownWindow();
                return;
            }

            if (_countdownWindow is { } current)
            {
                current.UpdateRemaining(remaining);
                return;
            }

            var window = new RecordingCountdownWindow(RecorderPlacementRegion());
            _countdownWindow = window;
            window.UpdateRemaining(remaining);
            window.Closed += CountdownWindowClosed;
            if (_sourceViewport is { IsVisible: true } viewport) window.Show(viewport);
            else if (IsVisible) window.Show(this);
            else window.Show();
        }

        private void CountdownWindowClosed(object? sender, EventArgs args)
        {
            _ = GuardAsync(_controller.StopAsync);
        }

        private void CloseCountdownWindow()
        {
            if (_countdownWindow is not { } window) return;
            _countdownWindow = null;
            window.Closed -= CountdownWindowClosed;
            if (window.IsVisible) window.Close();
        }
    }
}
