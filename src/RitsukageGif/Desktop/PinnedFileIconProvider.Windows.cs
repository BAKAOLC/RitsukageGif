using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedFileIconProvider
    {
        private const uint ShellIcon = 0x100;
        private const uint ShellSmallIcon = 1;
        private const uint ShellUseFileAttributes = 0x10;
        private const uint DirectoryAttribute = 0x10;
        private const uint NormalFileAttribute = 0x80;
        private const uint DrawNormalIcon = 3;
        private const int ComChangedMode = unchecked((int)0x80010106);

        [SupportedOSPlatform("windows")]
        private static unsafe byte[] ReadWindows(string path, int size, CancellationToken token)
        {
            var initialized = CoInitializeEx(0, 0);
            if (initialized < 0 && initialized != ComChangedMode) Marshal.ThrowExceptionForHR(initialized);
            try
            {
                var flags = ShellIcon | (size <= 16 ? ShellSmallIcon : 0);
                if (size > 32) flags |= ShellSystemIconIndex;
                if (!File.Exists(path) && !Directory.Exists(path)) flags |= ShellUseFileAttributes;
                if (ShGetFileInfo(path,
                        path.EndsWith(Path.DirectorySeparatorChar) ? DirectoryAttribute : NormalFileAttribute,
                        out var info, (uint)sizeof(FileIconShellInfo), flags) == 0 || info.Icon == 0)
                    throw new IOException("The Windows Shell returned no file icon.");
                nint imageListIcon = 0;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var icon = info.Icon;
                    var width = size;
                    var height = size;
                    if (size > 32)
                    {
                        var larger = ReadWindowsImageListIcon(info.Index, size);
                        icon = imageListIcon = larger.Icon;
                        width = larger.Width;
                        height = larger.Height;
                    }

                    var black = DrawWindowsIcon(icon, width, height, 0);
                    var white = DrawWindowsIcon(icon, width, height, 255);
                    for (var offset = 0; offset < black.Length; offset += 4)
                    {
                        var alpha = Math.Clamp(255 + black[offset] - white[offset], 0, 255);
                        for (var channel = 0; channel < 3; channel++)
                            black[offset + channel] = alpha == 0
                                ? (byte)0
                                : (byte)Math.Clamp((black[offset + channel] * 255 + alpha / 2) / alpha, 0, 255);
                        black[offset + 3] = (byte)alpha;
                    }

                    using var bitmap = new SKBitmap(new(width, height, SKColorType.Bgra8888,
                        SKAlphaType.Unpremul));
                    Marshal.Copy(black, 0, bitmap.GetPixels(), black.Length);
                    using var image = SKImage.FromBitmap(bitmap);
                    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                    return encoded.ToArray();
                }
                finally
                {
                    if (imageListIcon != 0 && DestroyIcon(imageListIcon) == 0)
                        Logger.ZLogWarning($"Windows file icon cleanup failed. Api={nameof(DestroyIcon)}");
                    if (DestroyIcon(info.Icon) == 0)
                        Logger.ZLogWarning($"Windows file icon cleanup failed. Api={nameof(DestroyIcon)}");
                }
            }
            finally
            {
                if (initialized >= 0) CoUninitialize();
            }
        }

        [SupportedOSPlatform("windows")]
        private static byte[] DrawWindowsIcon(nint icon, int width, int height, byte background)
        {
            var dc = CreateCompatibleDC(0);
            var info = new FileIconBitmapInfo
            {
                Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32,
            };
            nint bitmap = 0;
            nint previous = 0;
            try
            {
                if (dc == 0) throw new IOException("The file icon drawing context could not be created.");
                bitmap = CreateDibSection(dc, ref info, 0, out var bits, 0, 0);
                if (bitmap == 0 || bits == 0) throw new IOException("The file icon bitmap could not be created.");
                previous = SelectObject(dc, bitmap);
                if (previous is 0 or -1) throw new IOException("The file icon bitmap could not be selected.");
                var pixels = new byte[width * height * 4];
                Array.Fill(pixels, background);
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                if (DrawIconEx(dc, 0, 0, icon, width, height, 0, 0, DrawNormalIcon) == 0 || GdiFlush() == 0)
                    throw new IOException("The Windows Shell file icon could not be drawn.");
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                return pixels;
            }
            finally
            {
                if (previous is not (0 or -1)) SelectObject(dc, previous);
                if (bitmap != 0 && DeleteObject(bitmap) == 0)
                    Logger.ZLogWarning($"Windows file icon cleanup failed. Api={nameof(DeleteObject)}");
                if (dc != 0 && DeleteDC(dc) == 0)
                    Logger.ZLogWarning($"Windows file icon cleanup failed. Api={nameof(DeleteDC)}");
            }
        }

        [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nuint ShGetFileInfo(string path, uint attributes, out FileIconShellInfo info,
            uint infoSize, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("user32.dll")]
        private static partial int DestroyIcon(nint icon);

        [LibraryImport("user32.dll")]
        private static partial int DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step,
            nint brush, uint flags);

        [LibraryImport("gdi32.dll")]
        private static partial nint CreateCompatibleDC(nint dc);

        [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
        private static partial nint CreateDibSection(nint dc, ref FileIconBitmapInfo info, uint usage, out nint bits,
            nint section, uint offset);

        [LibraryImport("gdi32.dll")]
        private static partial nint SelectObject(nint dc, nint value);

        [LibraryImport("gdi32.dll")]
        private static partial int DeleteObject(nint value);

        [LibraryImport("gdi32.dll")]
        private static partial int DeleteDC(nint dc);

        [LibraryImport("gdi32.dll")]
        private static partial int GdiFlush();
    }
}
