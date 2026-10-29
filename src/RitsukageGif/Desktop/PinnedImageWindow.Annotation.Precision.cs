namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private Task<PngRaster>? _annotationPrecisionLoad;
        private byte[]? _annotationPrecisionPng;
        private ScreenshotAnnotationSession? _annotationPrecisionSession;
        private PngRaster? _annotationPrecisionSource;

        private bool HasAnnotationPrecisionSource => _hdrSource is null && _png.Length >= 33 && _png[24] == 16 &&
                                                     _png.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)
                                                         [137, 80, 78, 71, 13, 10, 26, 10]);

        private async Task<PngRaster?> GetAnnotationPrecisionSourceAsync(ScreenshotAnnotationSession session,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HasAnnotationPrecisionSource || !ReferenceEquals(session, _annotation) ||
                _annotationCancellation is null)
                return null;
            var png = _png;
            if (!ReferenceEquals(_annotationPrecisionPng, png) ||
                !ReferenceEquals(_annotationPrecisionSession, session))
            {
                _annotationPrecisionPng = png;
                _annotationPrecisionSession = session;
                _annotationPrecisionSource = null;
                var lifetime = _annotationCancellation.Token;
                _annotationPrecisionLoad = Task.Run(() =>
                        PngColorTransform.NormalizeAnnotationRaster(PngImageCodec.Decode(png, lifetime), lifetime),
                    lifetime);
            }

            if (_annotationPrecisionSource is { } cached) return cached;
            var loaded = await _annotationPrecisionLoad!.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (_closed || !ReferenceEquals(session, _annotation) || !ReferenceEquals(png, _png) ||
                !ReferenceEquals(session, _annotationPrecisionSession) ||
                !ReferenceEquals(png, _annotationPrecisionPng))
                throw new OperationCanceledException(token);
            if (loaded.Width != (int)_bitmap.Size.Width || loaded.Height != (int)_bitmap.Size.Height)
                throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
            _annotationPrecisionSource = loaded;
            return loaded;
        }

        private void ClearAnnotationPrecisionSource()
        {
            _annotationPrecisionLoad = null;
            _annotationPrecisionPng = null;
            _annotationPrecisionSession = null;
            _annotationPrecisionSource = null;
        }
    }
}
