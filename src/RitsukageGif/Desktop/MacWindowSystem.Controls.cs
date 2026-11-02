using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class MacWindowSystem
    {
        private static bool _controlsAvailable;
        internal static bool ControlDetectionAvailable => Volatile.Read(ref _controlsAvailable);

        internal static bool CheckControlPermission(bool request = false)
        {
            using var result = Query(request ? "request-control-permission" : "control-capability");
            var available = result.RootElement.GetProperty("available").GetBoolean();
            Volatile.Write(ref _controlsAvailable, available);
            return available;
        }

        internal static IReadOnlyList<WindowControlTarget> Controls(WindowTarget target, PixelPoint point,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var result = Query(string.Create(CultureInfo.InvariantCulture,
                $"controls {(long)target.Handle} {point.X} {point.Y}"));
            token.ThrowIfCancellationRequested();
            var available = result.RootElement.GetProperty("available").GetBoolean();
            Volatile.Write(ref _controlsAvailable, available);
            if (!available) throw new IOException(UiText.Get("PlatformAccessibilityPermission"));
            var values = new List<WindowControlTarget>();
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var item in result.RootElement.GetProperty("controls").EnumerateArray())
            {
                var bounds = Rectangle(item).Intersect(target.Bounds);
                if (!bounds.IsEmpty && bounds.Contains(point)) values.Add(new(target, 0, bounds));
            }

            return values;
        }

        private static JsonDocument Query(string command)
        {
            lock (Sync)
            {
                if (_server is null || _server.HasExited)
                {
                    _updated = 0;
                    List();
                }

                try
                {
                    _server!.StandardInput.WriteLine(command);
                    _server.StandardInput.Flush();
                    var line = _server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3))
                        .GetAwaiter().GetResult() ?? throw new IOException(UiText.Get("CaptureFailed"));
                    return JsonDocument.Parse(line);
                }
                catch
                {
                    CloseCore();
                    throw;
                }
            }
        }
    }
}
