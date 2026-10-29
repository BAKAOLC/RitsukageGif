using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    // Frames are immutable after decoding; callers must not mutate or dispose returned bitmaps.
    internal sealed class MediaFrameSource : IDisposable
    {
        private readonly List<double> _ends = [];
        private readonly List<SKBitmap> _frames = [];
        private bool _disposed;

        public MediaFrameSource(string path, long maxDecodedBytes = 512L * 1024 * 1024,
            CancellationToken token = default)
        {
            if (maxDecodedBytes is < 0 or > 512L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
            using var codec = SKCodec.Create(path) ?? throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var bytesPerFrame = checked((long)codec.Info.Width * codec.Info.Height * 4);
            if (bytesPerFrame <= 0 || bytesPerFrame > maxDecodedBytes ||
                bytesPerFrame * Math.Max(1, codec.FrameCount) > maxDecodedBytes)
                throw new InvalidDataException(UiText.Get("RecordingTooLarge"));
            long allocated = 0;
            double elapsed = 0;
            try
            {
                foreach (var frame in PreviewDecoder.Read(path, token))
                    using (frame)
                    {
                        allocated = checked(allocated + (long)frame.Width * frame.Height * 4);
                        if (allocated > maxDecodedBytes)
                            throw new InvalidDataException(UiText.Get("RecordingTooLarge"));
                        var bitmap = new SKBitmap(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        try
                        {
                            Marshal.Copy(frame.Pixels, 0, bitmap.GetPixels(), checked(frame.Width * frame.Height * 4));
                            _frames.Add(bitmap);
                            DecodedBytes = allocated;
                        }
                        catch
                        {
                            bitmap.Dispose();
                            throw;
                        }

                        elapsed += frame.Delay / 1000.0;
                        _ends.Add(elapsed);
                    }

                if (_frames.Count == 0) throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int Width => _frames[0].Width;
        public int Height => _frames[0].Height;
        private double Duration => _ends[^1];
        public long DecodedBytes { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var frame in _frames) frame.Dispose();
            _frames.Clear();
            DecodedBytes = 0;
        }

        public SKBitmap? FrameAt(double elapsedSeconds, bool loop = true, double speed = 1,
            double position = 0, bool blankAfterEnd = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(speed) || !double.IsFinite(position) || speed < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (_frames.Count == 1) return _frames[0];
            var time = Math.Max(0, elapsedSeconds * speed + position);
            if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (loop) time %= Duration;
            else if (time >= Duration) return blankAfterEnd ? null : _frames[^1];
            var index = _ends.BinarySearch(time);
            index = index >= 0 ? index + 1 : ~index;
            return _frames[Math.Min(index, _frames.Count - 1)];
        }
    }
}
