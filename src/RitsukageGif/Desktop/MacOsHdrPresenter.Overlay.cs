using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class MacOsHdrPresenter
    {
        internal nint CreateOverlay(nint parentWindow)
        {
            nint result;
            try
            {
                result = CreateOverlayNative(View, parentWindow);
            }
            catch (EntryPointNotFoundException error)
            {
                throw new NotSupportedException("The macOS HDR preview background is unavailable.", error);
            }

            if (result == 0) throw new NotSupportedException("The macOS HDR preview background could not be created.");
            return result;
        }

        internal void SetPatch(CapturedFrame? source, PixelRect bounds, PixelRect region)
        {
            int result;
            if (source is null) result = Patch(View, null, 0, 0, 0, 0, 0, 0, 0, 2);
            else
                fixed (byte* data = source.Pixels.Span)
                    result = Patch(View, data, source.Width, source.Height, source.Stride,
                        (float)(bounds.X - region.X) / region.Width, (float)(bounds.Y - region.Y) / region.Height,
                        (float)bounds.Width / region.Width, (float)bounds.Height / region.Height,
                        source.AlphaMode switch
                        {
                            CaptureAlphaMode.Straight => 1, CaptureAlphaMode.Opaque => 2, _ => 0,
                        });
            if (result < 0) throw new ExternalException("The macOS HDR preview patch could not be uploaded.", result);
        }

        internal void SetSelection(PixelRect selection, PixelRect region)
        {
            Selection(View, (float)(selection.X - region.X) / region.Width,
                (float)(selection.Y - region.Y) / region.Height,
                (float)selection.Width / region.Width, (float)selection.Height / region.Height);
        }

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_overlay_create")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial nint CreateOverlayNative(nint view, nint parentWindow);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_overlay_show")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void ShowOverlay(nint overlay, int visible);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_overlay_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void DestroyOverlay(nint overlay);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_patch")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int Patch(nint view, byte* pixels, int width, int height, int stride,
            float x, float y, float w, float h, int alphaMode);

        [LibraryImport("RitsukageHdrPreview", EntryPoint = "rg_hdr_selection")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void Selection(nint view, float x, float y, float w, float h);
    }
}
