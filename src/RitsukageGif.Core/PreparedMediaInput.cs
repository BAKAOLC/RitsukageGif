using ImageMagick;

namespace RitsukageGif.Core
{
    // Scoped to one serial batch or optimization. Outputs clone these pixels before modifying them.
    internal sealed class PreparedMediaInput(MediaInfo info, int? frameIndex = null) : IDisposable
    {
        private MagickImageCollection? _images;
        private bool _disposed;

        internal PreparedMediaInput(MediaInfo info, int frameIndex, IMagickImage<byte> image) : this(info, frameIndex)
        {
            _images = new MagickImageCollection { image.Clone() };
        }

        internal MediaInfo Info { get; } = info;
        internal int? FrameIndex { get; } = info.Format == "apng" ? frameIndex : null;

        internal MagickImageCollection GetImages(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            return _images ??= FrameIndex is { } selected
                ? MediaPngCodec.ReadFrame(Info.Path, selected, Info, token)
                : MediaProcessingService.Decode(Info.Path, Info, token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _images?.Dispose();
            _images = null;
        }
    }
}
