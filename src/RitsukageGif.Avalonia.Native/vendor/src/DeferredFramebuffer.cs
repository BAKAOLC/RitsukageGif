using System;
using System.Runtime.InteropServices;
using Avalonia.Native.Interop;
using Avalonia.Platform;

namespace Avalonia.Native
{
    internal unsafe class DeferredFramebuffer : ILockedFramebuffer
    {
        private readonly IAvnSoftwareRenderTarget _renderTarget;
        private readonly Action<Action<IAvnTopLevel>> _lockTopLevel;

        // ReSharper disable once ConvertToPrimaryConstructor
        public DeferredFramebuffer(IAvnSoftwareRenderTarget renderTarget, Action<Action<IAvnTopLevel>> lockTopLevel,
            int width, int height, Vector dpi)
        {
            _renderTarget = renderTarget;
            _lockTopLevel = lockTopLevel;
            Address = Marshal.AllocHGlobal(width * height * 4);
            Size = new(width, height);
            RowBytes = width * 4;
            Dpi = dpi;
            Format = PixelFormat.Bgra8888;
        }

        public IntPtr Address { get; private set; }
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi { get; }
        public PixelFormat Format { get; }
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;

        public void Dispose()
        {
            if (Address == IntPtr.Zero)
                return;

            _lockTopLevel(_ =>
            {
                var fb = new AvnFramebuffer
                {
                    Data = Address.ToPointer(),
                    Dpi = new() { X = Dpi.X, Y = Dpi.Y },
                    Width = Size.Width,
                    Height = Size.Height,
                    PixelFormat = (AvnPixelFormat)Format.FormatEnum,
                    Stride = RowBytes,
                };

                _renderTarget.SetFrame(&fb);
            });

            Marshal.FreeHGlobal(Address);

            Address = IntPtr.Zero;
        }
    }
}
