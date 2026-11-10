using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingCursorSampler : IDisposable
    {
        private readonly X11Capture.RecordingCursorReader? _x11;
        private readonly Process? _mac;
        private readonly NativeCapturePacketReader? _reader;
        private readonly Task<string>? _errors;
        private readonly Func<NativeCaptureCursorFrame?>? _nativeCursorProvider;
        private readonly Func<string?>? _nativeCursorReasonProvider;
        private RecordingCursorSample _shape = RecordingCursorSample.Hidden;
        private nint _windowsHandle;
        private ulong _shapeId;

        internal RecordingCursorSampler(Func<NativeCaptureCursorFrame?>? nativeCursorProvider = null,
            Func<string?>? nativeCursorReasonProvider = null)
        {
            _nativeCursorProvider = nativeCursorProvider;
            _nativeCursorReasonProvider = nativeCursorReasonProvider;
            if (OperatingSystem.IsMacOS())
            {
                var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture"))
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                start.ArgumentList.Add("recording-cursor");
                _mac = Process.Start(start) ?? throw new IOException("Cannot start the recording cursor helper.");
                _reader = new(_mac.StandardOutput.BaseStream);
                _errors = _mac.StandardError.ReadToEndAsync();
            }
            else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
            {
                _x11 = new();
            }
        }

        internal string? UnavailableReason => DesktopCapture.UsesSourceSelection
            ? _nativeCursorProvider is null
                ? NativeCaptureSession.RecordingCursorUnavailableReason
                : _nativeCursorReasonProvider?.Invoke()
            : null;

        public void Dispose()
        {
            _x11?.Dispose();
            if (_mac is null) return;
            _mac.StandardInput.Dispose();
            if (!_mac.WaitForExit(500)) _mac.Kill();
            _mac.Dispose();
            _ = _errors;
        }

        internal RecordingCursorSample Capture(bool includeButtons, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                var cursor = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
                if (NativeMethods.GetCursorInfo(ref cursor) == 0) return RecordingCursorSample.Hidden;
                if ((cursor.Flags & 1) == 0)
                    return RecordingCursorSample.Hidden with { X = cursor.Position.X, Y = cursor.Position.Y };
                if (_windowsHandle != cursor.Cursor || _shape.Pixels.Length == 0)
                {
                    using var frozen = FrozenScreenshotCursor.CaptureWindows(cursor);
                    if (frozen is null)
                        return RecordingCursorSample.Hidden with { X = cursor.Position.X, Y = cursor.Position.Y };
                    var (pixels, andMask) = frozen.ReadRecordingPixels();
                    _windowsHandle = cursor.Cursor;
                    _shape = new(++_shapeId, true, cursor.Position.X, cursor.Position.Y,
                        frozen.Bounds.Width, frozen.Bounds.Height, cursor.Position.X - frozen.Bounds.X,
                        cursor.Position.Y - frozen.Bounds.Y, 1, pixels, andMask);
                }

                return _shape with
                {
                    X = cursor.Position.X, Y = cursor.Position.Y,
                    Buttons = includeButtons
                        ? ((NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0 ? 1 : 0) |
                          ((NativeMethods.GetAsyncKeyState(2) & 0x8000) != 0 ? 2 : 0) |
                          ((NativeMethods.GetAsyncKeyState(4) & 0x8000) != 0 ? 4 : 0)
                        : 0,
                };
            }

            if (_x11 is not null) return _x11.Read(includeButtons);
            if (_mac is not null)
            {
                _mac.StandardInput.WriteLine("cursor");
                _mac.StandardInput.Flush();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var packet = _reader!.ReadAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                return ReadNative(packet.Metadata.Cursor, packet.Cursor) with
                {
                    Buttons = includeButtons ? MacButtons() : 0,
                };
            }

            var shared = _nativeCursorProvider is null
                ? NativeCaptureSession.ReadSharedCursor()
                : _nativeCursorProvider();
            return ReadNative(shared?.Cursor, shared?.Pixels ?? ReadOnlyMemory<byte>.Empty);
        }

        private RecordingCursorSample ReadNative(NativeCaptureCursor? cursor, ReadOnlyMemory<byte> pixels)
        {
            if (cursor is null) return RecordingCursorSample.Hidden;
            if (!cursor.Valid || !cursor.Visible)
                return RecordingCursorSample.Hidden with { X = cursor.X, Y = cursor.Y };
            _shape = pixels.IsEmpty switch
            {
                false when _shape.ShapeId != cursor.BitmapId || _shape.Pixels.Length == 0 =>
                    new(cursor.BitmapId, true, cursor.X, cursor.Y, cursor.Width, cursor.Height,
                        cursor.HotspotX, cursor.HotspotY, cursor.PixelScale, pixels.ToArray()),
                true when _shape.ShapeId != cursor.BitmapId =>
                    throw new InvalidDataException("The recording cursor references a missing bitmap."),
                _ => _shape,
            };
            return _shape with { X = cursor.X, Y = cursor.Y, Visible = true, PixelScale = cursor.PixelScale };
        }
    }
}
