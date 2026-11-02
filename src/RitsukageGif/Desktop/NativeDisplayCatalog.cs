using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal static class NativeDisplayCatalog
    {
        private static readonly Lock Gate = new();
        private static Dictionary<PixelRect, uint> _displays = [];
        private static Dictionary<uint, NativeCaptureGeometry> _native = [];

        public static void Update(Screens screens)
        {
            if (!OperatingSystem.IsMacOS()) return;
            Dispatcher.UIThread.VerifyAccess();
            var displays = new Dictionary<PixelRect, uint>();
            foreach (var screen in screens.All)
            {
                var handle = screen.TryGetPlatformHandle();
                if (handle is null || handle.Handle == 0) continue;
                displays[new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)] =
                    checked((uint)handle.Handle);
            }

            lock (Gate)
            {
                _displays = displays;
                _native = [];
            }
        }

        public static NativeCaptureGeometry Resolve(PixelRect bounds)
        {
            lock (Gate)
            {
                if (!_displays.TryGetValue(bounds, out var id)) throw new CaptureSourceLostException();
                if (_native.Count == 0)
                {
                    var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture"))
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    start.ArgumentList.Add("displays");
                    using var process = Process.Start(start) ??
                                        throw new IOException(UiText.Get("CaptureHelperMissing"));
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        throw new IOException(UiText.Get("CaptureSourceTimeout"));
                    }

                    if (process.ExitCode != 0) throw new IOException(errors.GetAwaiter().GetResult());
                    var values = JsonSerializer.Deserialize(output.GetAwaiter().GetResult(),
                        NativeCaptureJsonContext.Default.NativeCaptureGeometryArray) ?? [];
                    foreach (var value in values) _native[value.DisplayId] = value;
                }

                return _native.TryGetValue(id, out var geometry) ? geometry : throw new CaptureSourceLostException();
            }
        }

        public static double Density(PixelRect region)
        {
            if (!OperatingSystem.IsMacOS()) return 1;
            PixelRect[] bounds;
            lock (Gate) bounds = [.. _displays.Keys];
            return bounds.Where(item => !item.Intersect(region).IsEmpty)
                .Select(item => Resolve(item).BackingScale).DefaultIfEmpty(1).Max();
        }
    }
}
