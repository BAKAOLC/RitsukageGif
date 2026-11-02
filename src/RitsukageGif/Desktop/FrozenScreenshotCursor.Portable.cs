using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class FrozenScreenshotCursor
    {
        private byte[]? _nativePixels;
        private int _nativeWidth, _nativeHeight;

        internal static FrozenScreenshotCursor? FromNative(NativeCaptureCursor? cursor, byte[]? pixels)
        {
            if (cursor is not { Valid: true, Visible: true, Width: > 0, Height: > 0 } || pixels is null) return null;
            var scale = cursor.PixelScale;
            var left = (int)Math.Floor(cursor.X - cursor.HotspotX / scale);
            var top = (int)Math.Floor(cursor.Y - cursor.HotspotY / scale);
            return new(0, new(left, top, Math.Max(1, (int)Math.Ceiling(cursor.Width / scale)),
                    Math.Max(1, (int)Math.Ceiling(cursor.Height / scale))))
                { _nativePixels = pixels, _nativeWidth = cursor.Width, _nativeHeight = cursor.Height };
        }

        private static FrozenScreenshotCursor? CapturePortable()
        {
            if (DesktopCapture.UsesSourceSelection)
            {
                var frame = NativeCaptureSession.ReadSharedCursor();
                return FromNative(frame?.Cursor, frame?.Pixels);
            }

            if (OperatingSystem.IsLinux()) return X11Capture.CaptureFrozenCursor();
            if (!OperatingSystem.IsMacOS()) return null;
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture"))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("cursor");
            using var process = Process.Start(start) ?? throw new IOException(UiText.Get("CaptureHelperMissing"));
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                using var packet = new NativeCapturePacketReader(process.StandardOutput.BaseStream)
                    .ReadAsync(timeout.Token)
                    .AsTask().GetAwaiter().GetResult();
                return FromNative(packet.Metadata.Cursor, packet.Cursor.ToArray());
            }
            finally
            {
                if (!process.WaitForExit(100)) process.Kill();
                _ = errors;
            }
        }

        private byte[] ReadNativeLogicalPixels()
        {
            lock (_sync)
            {
                var output = new byte[checked(_bounds.Width * _bounds.Height * 4)];
                DrawNative(output, _bounds, _bounds.Width, _bounds.Height);
                return output;
            }
        }

        internal void DrawScaled(byte[] pixels, PixelRect region, int width, int height)
        {
            lock (_sync)
                if (_nativePixels is not null) DrawNative(pixels, region, width, height);
                else if (width == region.Width && height == region.Height) Draw(pixels, region);
        }

        internal (PixelRect Area, byte[] Pixels)? CreateDensityOverlay(PixelRect firstRegion, double density,
            int offsetX, int offsetY, int outputWidth, int outputHeight)
        {
            lock (_sync)
            {
                var left = (int)Math.Floor((_bounds.X - firstRegion.X) * density) - offsetX;
                var top = (int)Math.Floor((_bounds.Y - firstRegion.Y) * density) - offsetY;
                var right = (int)Math.Ceiling((_bounds.Right - firstRegion.X) * density) - offsetX;
                var bottom = (int)Math.Ceiling((_bounds.Bottom - firstRegion.Y) * density) - offsetY;
                var area = new PixelRect(left, top, right - left, bottom - top)
                    .Intersect(new(0, 0, outputWidth, outputHeight));
                if (area.IsEmpty) return (area, []);
                var source = _nativePixels ?? ReadTransparentPixels();
                if (source is null) return null;
                var sourceWidth = _nativePixels is null ? _bounds.Width : _nativeWidth;
                var sourceHeight = _nativePixels is null ? _bounds.Height : _nativeHeight;
                var pixels = new byte[checked(area.Width * area.Height * 4)];
                for (var y = 0; y < area.Height; y++)
                for (var x = 0; x < area.Width; x++)
                {
                    var worldX = firstRegion.X + (area.X + x + offsetX + .5) / density;
                    var worldY = firstRegion.Y + (area.Y + y + offsetY + .5) / density;
                    var sx = Math.Clamp((int)((worldX - _bounds.X) * sourceWidth / _bounds.Width), 0, sourceWidth - 1);
                    var sy = Math.Clamp((int)((worldY - _bounds.Y) * sourceHeight / _bounds.Height), 0,
                        sourceHeight - 1);
                    source.AsSpan((sy * sourceWidth + sx) * 4, 4).CopyTo(pixels.AsSpan((y * area.Width + x) * 4, 4));
                }

                return (area, pixels);
            }
        }

        private void DrawNative(byte[] output, PixelRect region, int width, int height)
        {
            lock (_sync)
            {
                if (_nativePixels is not { } source) return;
                var clipped = region.Intersect(_bounds);
                if (clipped.IsEmpty) return;
                var left = Math.Max(0, (int)Math.Floor((double)(clipped.X - region.X) * width / region.Width));
                var top = Math.Max(0, (int)Math.Floor((double)(clipped.Y - region.Y) * height / region.Height));
                var right = Math.Min(width,
                    (int)Math.Ceiling((double)(clipped.Right - region.X) * width / region.Width));
                var bottom = Math.Min(height,
                    (int)Math.Ceiling((double)(clipped.Bottom - region.Y) * height / region.Height));
                for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                {
                    var sx = Math.Clamp(
                        (int)(((x + .5) * region.Width / width + region.X - _bounds.X) * _nativeWidth / _bounds.Width),
                        0, _nativeWidth - 1);
                    var sy = Math.Clamp(
                        (int)(((y + .5) * region.Height / height + region.Y - _bounds.Y) * _nativeHeight /
                              _bounds.Height), 0, _nativeHeight - 1);
                    var sourceIndex = (sy * _nativeWidth + sx) * 4;
                    var target = (y * width + x) * 4;
                    var alpha = source[sourceIndex + 3];
                    var backgroundAlpha = output[target + 3];
                    var resultAlpha = alpha + (backgroundAlpha * (255 - alpha) + 127) / 255;
                    for (var c = 0; c < 3; c++)
                        output[target + c] = resultAlpha == 0
                            ? (byte)0
                            : (byte)((source[sourceIndex + c] * alpha +
                                      output[target + c] * backgroundAlpha * (255 - alpha) / 255 + resultAlpha / 2) /
                                     resultAlpha);
                    output[target + 3] = (byte)resultAlpha;
                }
            }
        }
    }
}
