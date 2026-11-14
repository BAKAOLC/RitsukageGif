using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class MacWindowSystem
    {
        private static readonly Lock Sync = new();
        private static Process? _server;
        private static IReadOnlyList<WindowTarget> _windows = [];
        private static long _updated;

        static MacWindowSystem()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Close();
        }

        internal static async Task EnsurePermissionAsync(CancellationToken token)
        {
            using var result = await ReadCommandAsync("request-permission", token).ConfigureAwait(false);
            if (!result.RootElement.GetBoolean()) throw new IOException(UiText.Get("PlatformCapturePermission"));
        }

        internal static IReadOnlyList<WindowTarget> List()
        {
            lock (Sync)
            {
                if (Stopwatch.GetElapsedTime(_updated).TotalMilliseconds < 100) return _windows;
                try
                {
                    if (_server is null || _server.HasExited)
                    {
                        CloseCore();
                        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Capture"))
                        {
                            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                            UseShellExecute = false,
                        };
                        start.ArgumentList.Add("window-server");
                        _server = Process.Start(start) ??
                                  throw new IOException(UiText.Get("PlatformCaptureHelperMissing"));
                        _server.ErrorDataReceived += (_, _) => { };
                        _server.BeginErrorReadLine();
                    }

                    _server.StandardInput.WriteLine("windows");
                    _server.StandardInput.Flush();
                    var line = _server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter()
                        .GetResult();
                    if (line is null)
                    {
                        _server.WaitForExit(100);
                        throw new IOException(UiText.Get(_server is { HasExited: true, ExitCode: 77 }
                            ? "PlatformCapturePermission"
                            : "CaptureFailed"));
                    }

                    if (line.Length > 1024 * 1024) throw new IOException(UiText.Get("CaptureFailed"));
                    using var document = JsonDocument.Parse(line);
                    var result = new List<WindowTarget>();
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var window in document.RootElement.EnumerateArray())
                    {
                        var handle = (nint)window.GetProperty("handle").GetInt64();
                        if (TransientWindowShell.IsTransient(handle)) continue;
                        var bounds = Rectangle(window.GetProperty("bounds"));
                        if (bounds.IsEmpty) continue;
                        result.Add(new(handle,
                            window.GetProperty("process").GetString() ?? "",
                            window.GetProperty("title").GetString() ?? "",
                            bounds, Rectangle(window.GetProperty("client")))
                        {
                            ProcessId = window.GetProperty("pid").GetUInt32(),
                        });
                    }

                    _windows = result;
                    _updated = Stopwatch.GetTimestamp();
                    return result;
                }
                catch
                {
                    CloseCore();
                    throw;
                }
            }
        }

        private static PixelRect Rectangle(JsonElement value)
        {
            return new(value.GetProperty("x").GetInt32(),
                value.GetProperty("y").GetInt32(), value.GetProperty("width").GetInt32(),
                value.GetProperty("height").GetInt32());
        }

        private static void Close()
        {
            lock (Sync) CloseCore();
        }

        private static void CloseCore()
        {
            if (_server is null) return;
            try
            {
                if (!_server.HasExited) _server.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _server.Dispose();
                _server = null;
            }
        }
    }
}
