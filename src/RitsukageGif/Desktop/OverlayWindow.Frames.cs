namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        internal void ReplacePixels(byte[] pixels)
        {
            ObjectDisposedException.ThrowIf(_imageDisposed, this);
            var frame = _coordinator.FrozenWindow;
            ScreenshotCursorOverlay? cursor = null;
            if (!_sourceLocal && _coordinator.ScreenshotCursor is { } screenshotCursor)
                cursor = new(screenshotCursor, Region, frame?.Pixels ?? pixels, _canvas.Scale, frame?.Region);
            try
            {
                _canvas.ReplacePixels(pixels);
            }
            catch
            {
                cursor?.Dispose();
                throw;
            }

            _screenPixels = pixels;
            if (_cursorOverlay is not null)
            {
                _layers.Children.Remove(_cursorOverlay);
                _cursorOverlay.Dispose();
            }

            _cursorOverlay = cursor;
            if (cursor is not null) _layers.Children.Insert(1, cursor);
            _hdrFailed = false;
            SuspendHdrBackground();
        }
    }
}
