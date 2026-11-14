namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PixelPoint _captureContentOffset;
        private PixelPoint? _capturePlacement;
        private bool _hasCenteredCaptureOrigin;
        private Avalonia.PixelPoint? _newPinCenter;

        internal void SetNewPinPlacement(PinnedDisplayPosition placement, PixelPoint? captureOrigin,
            PixelPoint contentOffset, Func<Avalonia.PixelPoint?> readPointer)
        {
            if (placement == PinnedDisplayPosition.ScreenshotLocation && captureOrigin is { } origin)
            {
                SetCapturePlacement(origin, contentOffset);
                return;
            }

            _hasCenteredCaptureOrigin = captureOrigin is not null;
            _newPinCenter = PinnedWindowPresentation.ResolveNewPinCenter(placement, Screens, readPointer);
            if (_newPinCenter is { } center) Position = center;
        }

        internal void SetCapturePlacement(PixelPoint outputOrigin, PixelPoint contentOffset)
        {
            if (contentOffset.X < 0 || contentOffset.Y < 0 ||
                (long)contentOffset.X * 2 >= _bitmap.Size.Width ||
                (long)contentOffset.Y * 2 >= _bitmap.Size.Height)
                throw new ArgumentOutOfRangeException(nameof(contentOffset));
            _capturePlacement = outputOrigin;
            _captureContentOffset = contentOffset;
            Position = Presentation.ContentOriginPosition(outputOrigin);
        }

        private void RestoreCapturePlacement()
        {
            _hasCenteredCaptureOrigin = false;
            if (_newPinCenter is { } center)
            {
                _newPinCenter = null;
                Presentation.SetScreenCenter(center);
                return;
            }

            if (_capturePlacement is not { } origin)
            {
                RestoreToScreen();
                return;
            }

            var left = (long)origin.X + _captureContentOffset.X;
            var top = (long)origin.Y + _captureContentOffset.Y;
            var right = origin.X + _bitmap.Size.Width - _captureContentOffset.X;
            var bottom = origin.Y + _bitmap.Size.Height - _captureContentOffset.Y;
            if (Screens.All.Any(screen => left < screen.Bounds.Right && right > screen.Bounds.X &&
                                          top < screen.Bounds.Bottom && bottom > screen.Bounds.Y))
                Position = Presentation.ContentOriginPosition(origin);
            else
                RestoreToScreen();
            _capturePlacement = null;
        }
    }
}
