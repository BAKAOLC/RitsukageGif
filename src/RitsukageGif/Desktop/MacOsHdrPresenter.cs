using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class MacOsHdrPresenter : IDisposable
    {
        internal MacOsHdrPresenter(delegate* unmanaged[Cdecl]<nint, void> changed, nint context)
        {
            try
            {
                View = Create(changed, context, Path.Combine(AppContext.BaseDirectory, "RitsukageHdrPreview.metallib"));
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new NotSupportedException("The macOS Metal HDR preview library is unavailable.", error);
            }

            if (View == 0) throw new NotSupportedException("The macOS Metal HDR preview could not be created.");
        }

        internal nint View { get; private set; }

        internal bool SupportsHdr => Headroom(View) > 1;

        public void Dispose()
        {
            if (View == 0) return;
            Destroy(View);
            View = 0;
        }

        internal void Present(ReadOnlySpan<byte> pixels, int width, int height, float referenceWhite,
            CaptureAlphaMode alphaMode, int stride = 0, bool smoothScaling = true)
        {
            ObjectDisposedException.ThrowIf(View == 0, this);
            if (stride == 0) stride = checked(width * 8);
            if (pixels.Length < (long)stride * height)
                throw new ArgumentException("Expected a complete FP16 RGBA frame.", nameof(pixels));
            fixed (byte* data = pixels)
            {
                var result = PresentFrame(View, data, width, height, stride, referenceWhite,
                    alphaMode switch
                    {
                        CaptureAlphaMode.Straight => 1, CaptureAlphaMode.Opaque => 2, _ => 0,
                    }, smoothScaling ? 1 : 0);
                if (result < 0)
                    throw new ExternalException("The macOS Metal HDR preview could not present a frame.", result);
            }
        }

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_create", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial nint Create(delegate* unmanaged[Cdecl]<nint, void> changed, nint context,
            string libraryPath);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_present")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int PresentFrame(nint view, byte* data, int width, int height, int stride, float white,
            int alphaMode, int smoothScaling);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_headroom")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial float Headroom(nint view);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void Destroy(nint view);
    }
}
