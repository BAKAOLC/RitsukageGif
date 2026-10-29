namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PixelPoint _captureContentOffset;
        private PixelPoint? _capturePlacement;

        internal void SetCapturePlacement(PixelPoint outputOrigin, PixelPoint contentOffset)
        {
            if (contentOffset.X < 0 || contentOffset.Y < 0 ||
                (long)contentOffset.X * 2 >= _bitmap.Size.Width ||
                (long)contentOffset.Y * 2 >= _bitmap.Size.Height)
                throw new ArgumentOutOfRangeException(nameof(contentOffset));
            _capturePlacement = outputOrigin;
            _captureContentOffset = contentOffset;
            Position = new(outputOrigin.X, outputOrigin.Y);
        }

        private void RestoreCapturePlacement()
        {
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
                Position = new(origin.X, origin.Y);
            else
                RestoreToScreen();
            _capturePlacement = null;
        }
    }
}
