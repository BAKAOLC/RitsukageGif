using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPinDecoder : IDisposable
    {
        private const long CacheBudget = 16L * 1024 * 1024;
        private readonly Dictionary<int, PreviewFrame> _cache = [];
        private readonly byte[] _encoded;
        private readonly AnimationPinMetadata _metadata;
        private readonly LinkedList<int> _recent = [];
        private long _cacheBytes;
        private SKBitmap? _canvas;
        private SKCodec? _codec;
        private bool _disposed;
        private byte[]? _linear;
        private int _next;
        private int _prior = -1;
        private SKBitmap? _restore;

        public AnimationPinDecoder(byte[] encoded, AnimationPinMetadata metadata)
        {
            ValidateGeometry(metadata, encoded.LongLength);
            _encoded = encoded;
            _metadata = metadata;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
            foreach (var frame in _cache.Values) frame.Dispose();
            _cache.Clear();
            _recent.Clear();
            _cacheBytes = 0;
        }

        internal static void ValidateGeometry(AnimationPinMetadata metadata, long encodedLength)
        {
            if (metadata.Width <= 0 || metadata.Height <= 0 || encodedLength is < 0 or > 256L * 1024 * 1024 ||
                (long)metadata.Width * metadata.Height > (512L * 1024 * 1024 - encodedLength) / 80)
                throw new InvalidDataException(UiText.Get("MemoryLimit"));
        }

        public PreviewFrame Read(int index, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((uint)index >= _metadata.Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
            token.ThrowIfCancellationRequested();
            if (_cache.TryGetValue(index, out var cached))
            {
                _recent.Remove(index);
                _recent.AddLast(index);
                return cached.Retain();
            }

            if (index < _next) Reset();
            Initialize();
            PreviewFrame? result = null;
            try
            {
                while (_next <= index)
                {
                    token.ThrowIfCancellationRequested();
                    result = _metadata.Extension == ".png"
                        ? ReadPng(_next, _next == index, token)
                        : ReadCodec(_next, _next == index, token);
                    _next++;
                }

                if (result is null) throw new InvalidDataException();
                if (result.StorageBytes <= CacheBudget)
                {
                    while (_cache.Count >= 32 || _cacheBytes + result.StorageBytes > CacheBudget)
                    {
                        var key = _recent.First!.Value;
                        _recent.RemoveFirst();
                        _cacheBytes -= _cache[key].StorageBytes;
                        _cache[key].Dispose();
                        _cache.Remove(key);
                    }

                    _cache.Add(index, result.Retain());
                    _cacheBytes += result.StorageBytes;
                    _recent.AddLast(index);
                }

                return result;
            }
            catch
            {
                result?.Dispose();
                Reset();
                throw;
            }
        }

        private void Initialize()
        {
            if (_canvas is not null || _linear is not null) return;
            if (_metadata.IsHdr)
            {
                _linear = new byte[checked(_metadata.Width * _metadata.Height * 8)];
            }
            else
            {
                _canvas = new(new(_metadata.Width, _metadata.Height, SKColorType.Bgra8888,
                    SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
                _canvas.Erase(SKColors.Transparent);
                _restore = new();
            }

            if (_metadata.Extension != ".png")
                _codec = SKCodec.Create(new MemoryStream(_encoded, false)) ?? throw new InvalidDataException();
        }

        private PreviewFrame? ReadCodec(int index, bool snapshot, CancellationToken token)
        {
            var canvas = _canvas!;
            var previous = _prior;
            var restore = _metadata.Frames[index].Disposal == 2;
            if (restore) Copy(canvas, _restore!);
            if (_codec!.GetPixels(canvas.Info, canvas.GetPixels(), new(index, _prior)) != SKCodecResult.Success)
                throw new InvalidDataException();
            var result = snapshot ? Snapshot(token) : null;
            if (restore)
            {
                Copy(_restore!, canvas);
                _prior = previous;
            }
            else
            {
                _prior = index;
            }

            return result;
        }

        private PreviewFrame Snapshot(CancellationToken token)
        {
            var length = checked(_metadata.Width * _metadata.Height * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                token.ThrowIfCancellationRequested();
                using var target = new SKBitmap(new(_metadata.Width, _metadata.Height, SKColorType.Bgra8888,
                    SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));
                using var source = _canvas!.PeekPixels();
                if (!source.ReadPixels(target.Info, target.GetPixels(), target.RowBytes, 0, 0))
                    throw new InvalidDataException();
                for (var y = 0; y < _metadata.Height; y++)
                    Marshal.Copy(target.GetPixels() + y * target.RowBytes, pixels, y * _metadata.Width * 4,
                        _metadata.Width * 4);
                return new(pixels, _metadata.Width, _metadata.Height, 0);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                throw;
            }
        }

        private static void Copy(SKBitmap source, SKBitmap target)
        {
            if (target.Info != source.Info && !target.TryAllocPixels(source.Info)) throw new OutOfMemoryException();
            using var pixels = source.PeekPixels();
            if (!pixels.ReadPixels(target.Info, target.GetPixels(), target.RowBytes, 0, 0))
                throw new InvalidDataException();
        }

        private void Reset()
        {
            _codec?.Dispose();
            _canvas?.Dispose();
            _restore?.Dispose();
            _codec = null;
            _canvas = null;
            _restore = null;
            _linear = null;
            _next = 0;
            _prior = -1;
        }
    }
}
