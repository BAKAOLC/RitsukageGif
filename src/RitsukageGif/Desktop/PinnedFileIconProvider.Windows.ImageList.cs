using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedFileIconProvider
    {
        private const uint ShellSystemIconIndex = 0x4000;
        private const int ShellExtraLargeImageList = 2;
        private const int ShellJumboImageList = 4;
        private const int ImageListReleaseSlot = 2;
        private const int ImageListGetIconSlot = 10;
        private const int ImageListGetIconSizeSlot = 16;
        private const uint ImageListTransparent = 1;

        private static readonly Guid ShellImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

        [SupportedOSPlatform("windows")]
        private static (nint Icon, int Width, int Height) ReadWindowsImageListIcon(int index, int requestedSize)
        {
            var list = OpenWindowsImageList(ShellExtraLargeImageList);
            try
            {
                var dimensions = ReadWindowsImageListDimensions(list);
                if (dimensions.Width >= requestedSize && dimensions.Height >= requestedSize)
                    return (CopyWindowsImageListIcon(list, index), dimensions.Width, dimensions.Height);
            }
            finally
            {
                ReleaseWindowsImageList(list);
            }

            list = OpenWindowsImageList(ShellJumboImageList);
            try
            {
                var dimensions = ReadWindowsImageListDimensions(list);
                if (dimensions.Width < requestedSize || dimensions.Height < requestedSize)
                    throw new IOException("The Shell image list is smaller than the requested file icon.");
                return (CopyWindowsImageListIcon(list, index), dimensions.Width, dimensions.Height);
            }
            finally
            {
                ReleaseWindowsImageList(list);
            }
        }

        [SupportedOSPlatform("windows")]
        private static nint OpenWindowsImageList(int kind)
        {
            Marshal.ThrowExceptionForHR(ShGetImageList(kind, in ShellImageListId, out var list));
            return list != 0 ? list : throw new IOException("The Shell returned no system image list.");
        }

        private static unsafe (int Width, int Height) ReadWindowsImageListDimensions(nint list)
        {
            var table = *(nint**)list;
            var getSize = (delegate* unmanaged[Stdcall]<nint, int*, int*, int>)table[ImageListGetIconSizeSlot];
            int width;
            int height;
            Marshal.ThrowExceptionForHR(getSize(list, &width, &height));
            return (width, height);
        }

        private static unsafe nint CopyWindowsImageListIcon(nint list, int index)
        {
            var table = *(nint**)list;
            var getIcon = (delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)table[ImageListGetIconSlot];
            nint icon;
            Marshal.ThrowExceptionForHR(getIcon(list, index, ImageListTransparent, &icon));
            return icon != 0 ? icon : throw new IOException("The Shell image list returned no associated file icon.");
        }

        private static unsafe void ReleaseWindowsImageList(nint list)
        {
            var table = *(nint**)list;
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)table[ImageListReleaseSlot];
            release(list);
        }

        [LibraryImport("shell32.dll", EntryPoint = "SHGetImageList")]
        private static partial int ShGetImageList(int kind, in Guid interfaceId, out nint list);
    }
}
