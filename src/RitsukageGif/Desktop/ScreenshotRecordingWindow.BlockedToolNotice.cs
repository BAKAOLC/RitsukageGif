using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private readonly Border _blockedToolNotice = new()
        {
            Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
            CornerRadius = new(5), Padding = new(7, 3), IsVisible = false, IsHitTestVisible = false,
            Child = new TextBlock { Foreground = AppTheme.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap },
        };

        private readonly DispatcherTimer _blockedToolNoticeTimer = new()
            { Interval = TimeSpan.FromMilliseconds(2200) };

        private void ShowBlockedRecordingToolNotice()
        {
            if (_closed || !IsVisible) return;
            ((TextBlock)_blockedToolNotice.Child!).Text = UiText.Get("RecordingAnnotationUnavailable");
            _blockedToolNotice.IsVisible = true;
            _blockedToolNoticeTimer.Stop();
            _blockedToolNoticeTimer.Start();
        }

        private void PlaceBlockedRecordingToolNotice(Canvas layer)
        {
            _blockedToolNotice.MaxWidth = Math.Max(0, layer.Bounds.Width * .8);
            Canvas.SetLeft(_blockedToolNotice, Math.Max(0, (layer.Bounds.Width - _blockedToolNotice.Bounds.Width) / 2));
            Canvas.SetTop(_blockedToolNotice, layer.Bounds.Height * .1);
        }

        private void BlockedRecordingToolNoticeTick(object? sender, EventArgs args)
        {
            HideBlockedRecordingToolNotice();
        }

        private void HideBlockedRecordingToolNotice()
        {
            _blockedToolNoticeTimer.Stop();
            _blockedToolNotice.IsVisible = false;
        }
    }
}
