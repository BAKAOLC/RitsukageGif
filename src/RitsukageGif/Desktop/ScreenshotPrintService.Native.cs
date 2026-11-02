using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotPrintService
    {
        [LibraryImport("comdlg32.dll")]
        private static partial int PrintDlgExW(ref PrintDialog dialog);

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();

        [LibraryImport("kernel32.dll")]
        private static partial nint GlobalFree(nint memory);

        [LibraryImport("user32.dll")]
        private static unsafe partial int EnumThreadWindows(uint thread,
            delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);

        [LibraryImport("user32.dll")]
        private static partial int PostMessageW(nint window, uint message, nint wParam, nint lParam);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static unsafe partial int SetAbortProc(nint context,
            delegate* unmanaged[Stdcall]<nint, int, int> callback);

        [LibraryImport("gdi32.dll")]
        private static partial int GetDeviceCaps(nint context, int index);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial int StartDocW(nint context, ref DocumentInfo info);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial int StartPage(nint context);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial int EndPage(nint context);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial int EndDoc(nint context);

        [LibraryImport("gdi32.dll")]
        private static partial int AbortDoc(nint context);

        [LibraryImport("gdi32.dll")]
        private static partial int DeleteDC(nint context);

        [LibraryImport("gdi32.dll")]
        private static partial int SetStretchBltMode(nint context, int mode);

        [LibraryImport("gdi32.dll")]
        private static partial int SetBrushOrgEx(nint context, int x, int y, nint previous);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        // ReSharper disable once InconsistentNaming
        private static partial int StretchDIBits(nint context, int x, int y, int width, int height, int sourceX,
            int sourceY, int sourceWidth, int sourceHeight, nint pixels, ref BitmapHeader header, uint usage,
            uint operation);

        [StructLayout(LayoutKind.Sequential)]
        private struct PrintDialog
        {
            internal uint Size;
            internal nint Owner, DeviceMode, DeviceNames, Context;
            internal uint Flags, Flags2, ExclusionFlags, PageRanges, MaximumPageRanges;
            internal nint Ranges;
            internal uint MinimumPage, MaximumPage, Copies;
            internal nint Instance, Template, Callback;
            internal uint PropertyPages;
            internal nint Pages;

            // ReSharper disable once MemberHidesStaticFromOuterClass
            internal uint StartPage, ResultAction;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DocumentInfo
        {
            internal int Size;
            internal nint Name, Output, Type;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapHeader
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, ImageSize;
            internal int XPixelsPerMeter, YPixelsPerMeter;
            internal uint ColorsUsed, ColorsImportant;
        }
    }
}
