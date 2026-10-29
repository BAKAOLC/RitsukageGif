using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageShadowStrip : Window, IDisposable
    {
        private readonly Image _image = new()
        {
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        private WriteableBitmap? _bitmap;
        private (int Width, int Height, int Margin, int Side, uint Color)? _cache;
        private bool _closed;
        private (int Width, int Height)? _nativeClipSize;
        private PixelRect _region;

        internal PinnedImageShadowStrip()
        {
            ShowActivated = false;
            TransientWindowShell.Configure(this);
            Focusable = false;
            IsHitTestVisible = false;
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            MinWidth = MinHeight = 0;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _image;
            ScalingChanged += (_, _) => UpdateDimensions();
            Closed += (_, _) => Release();
        }

        public void Dispose()
        {
            if (_closed) return;
            Close();
            Release();
        }

        internal void Refresh(Window owner, PixelRect region, int width, int height, int margin, int side, uint color)
        {
            if (_closed) return;
            var key = (width, height, margin, side, color);
            if (_cache != key)
            {
                var pixels = PinnedImageShadowRaster.Create(width, height, margin, side, color);
                var bitmap = new WriteableBitmap(new(region.Width, region.Height), new(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                try
                {
                    using var framebuffer = bitmap.Lock();
                    for (var y = 0; y < region.Height; y++)
                        Marshal.Copy(pixels, y * region.Width * 4, framebuffer.Address + y * framebuffer.RowBytes,
                            region.Width * 4);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }

                var previous = _bitmap;
                _image.Source = _bitmap = bitmap;
                _cache = key;
                previous?.Dispose();
            }

            _region = region;
            Position = new(region.X, region.Y);
            Topmost = owner.Topmost;
            Opacity = owner.Opacity;
            UpdateDimensions();
            if (!IsVisible)
            {
                ConfigureNativeInput();
                Show(owner);
                ConfigureNativeInput();
                UpdateDimensions();
            }
        }

        private void Release()
        {
            _closed = true;
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _cache = null;
        }

        private void UpdateDimensions()
        {
            if (_closed || _region.IsEmpty) return;
            Width = _region.Width / RenderScaling;
            Height = _region.Height / RenderScaling;
            _image.Width = _region.Width / RenderScaling;
            _image.Height = _region.Height / RenderScaling;
            EnforceNativeBounds();
        }

        private void EnforceNativeBounds()
        {
            if (!IsVisible || !OperatingSystem.IsWindows() ||
                TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;
            if (!SetWindowPos(handle.Handle, 0, _region.X, _region.Y, _region.Width, _region.Height, 0x0214))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var size = (_region.Width, _region.Height);
            if (_nativeClipSize == size) return;
            var clip = CreateRectRgn(0, 0, size.Width, size.Height);
            if (clip == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (SetWindowRgn(handle.Handle, clip, 1) == 0)
            {
                _ = DeleteObject(clip);
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _nativeClipSize = size;
        }

        private void ConfigureNativeInput()
        {
            if (!OperatingSystem.IsWindows() ||
                TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;
            ConfigureNativeInput(handle.Handle);
        }

        private static void ConfigureNativeInput(nint window)
        {
            var style = GetWindowLongPtr(window, -20);
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongPtr(window, -20, style | 0x08000000 | 0x80 | 0x20) == 0 &&
                Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetWindowRgn(nint window, nint region, int redraw);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(nint value);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowLongPtr(nint window, int index, nint value);
    }
}
