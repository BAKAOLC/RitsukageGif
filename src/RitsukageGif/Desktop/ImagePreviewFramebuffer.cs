using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class ImagePreviewFramebuffer : ILockedFramebuffer
    {
        private readonly bool _pooled;
        private GCHandle _pin;

        public ImagePreviewFramebuffer(PixelSize size, bool pooled = false)
        {
            Size = size;
            _pooled = pooled;
            var length = checked(size.Width * size.Height * 4);
            Pixels = pooled ? ArrayPool<byte>.Shared.Rent(length) : new byte[length];
            _pin = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
        }

        public byte[] Pixels { get; }
        public nint Address => _pin.AddrOfPinnedObject();
        public int RowBytes => checked(Size.Width * 4);
        public PixelSize Size { get; }
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Unpremul;

        public void Dispose()
        {
            if (!_pin.IsAllocated) return;
            _pin.Free();
            if (_pooled) ArrayPool<byte>.Shared.Return(Pixels);
        }
    }
}
