using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class LinuxAccessibilityLocator
    {
        private static readonly SemaphoreSlim Query = new(1, 1);
        private static readonly SemaphoreSlim Probe = new(1, 1);
        private static int _available;
        private static string Executable => Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Accessibility");
        internal static bool Available => Volatile.Read(ref _available) == 1;

        internal static async Task<bool> EnsureAvailableAsync(CancellationToken token = default)
        {
            if (Available) return true;
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                SetAvailability(false);
                return false;
            }

            await Probe.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Available) return true;
                var result = await RunAsync(["--probe"], 750, token).ConfigureAwait(false);
                var ready = result is { Code: 0, Output: var text } && text.Trim() == "AVAILABLE";
                SetAvailability(ready);
                return ready;
            }
            finally
            {
                Probe.Release();
            }
        }

        internal static async Task<IReadOnlyList<WindowControlTarget>> FindHierarchyAsync(WindowTarget window,
            PixelPoint point, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsLinux() || window.Handle == 0 || window.Bounds.IsEmpty ||
                !window.Bounds.Contains(point) || !await EnsureAvailableAsync(token).ConfigureAwait(false) ||
                !await Query.WaitAsync(0, token).ConfigureAwait(false)) return [];
            try
            {
                var result = await RunAsync([
                    ((nuint)window.Handle).ToString("X", CultureInfo.InvariantCulture),
                    point.X.ToString(CultureInfo.InvariantCulture), point.Y.ToString(CultureInfo.InvariantCulture),
                    window.Title,
                ], 250, token).ConfigureAwait(false);
                if (result is not { Code: 0 })
                {
                    if (result is { Code: 2 })
                        SetAvailability(false);
                    return [];
                }

                return Parse(window, point, result.Value.Output);
            }
            finally
            {
                Query.Release();
            }
        }

        private static void SetAvailability(bool available)
        {
            Volatile.Write(ref _available, available ? 1 : 0);
        }

        private static List<WindowControlTarget> Parse(WindowTarget window, PixelPoint point, string output)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length is < 2 or > 65) return [];
            var frame = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (frame.Length != 9 || frame[0] != "FRAME") return [];
            var values = new int[8];
            for (var index = 0; index < values.Length; index++)
                if (!int.TryParse(frame[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out values[index]))
                    return [];
            if (values[2] <= 0 || values[3] <= 0 || values[6] <= 0 || values[7] <= 0) return [];
            var clip = window.Bounds.Intersect(new(values[0], values[1], values[2], values[3]));
            if (clip.IsEmpty || !clip.Contains(point)) return [];
            var result = new List<WindowControlTarget>();
            foreach (var line in lines.Skip(1))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields is not ["RECT", _, _, _, _]) return [];
                var rect = new int[4];
                for (var index = 0; index < rect.Length; index++)
                    if (!int.TryParse(fields[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out rect[index]))
                        return [];
                if (rect[2] <= 0 || rect[3] <= 0) continue;
                var left = values[0] + ((double)rect[0] - values[4]) * values[2] / values[6];
                var top = values[1] + ((double)rect[1] - values[5]) * values[3] / values[7];
                var right = values[0] + ((double)rect[0] + rect[2] - values[4]) * values[2] / values[6];
                var bottom = values[1] + ((double)rect[1] + rect[3] - values[5]) * values[3] / values[7];
                left = Math.Max(left, clip.X);
                top = Math.Max(top, clip.Y);
                right = Math.Min(right, clip.Right);
                bottom = Math.Min(bottom, clip.Bottom);
                if (right <= left || bottom <= top) continue;
                var x = (int)Math.Floor(left);
                var y = (int)Math.Floor(top);
                var r = (int)Math.Ceiling(right);
                var b = (int)Math.Ceiling(bottom);
                if (r <= x || b <= y) continue;
                var bounds = new PixelRect(x, y, r - x, b - y).Intersect(clip);
                if (bounds.IsEmpty || !bounds.Contains(point)) continue;
                if (result.Count > 0 && result[^1].Bounds == bounds) continue;
                result.Add(new(window, 0, bounds));
                clip = bounds;
            }

            return result;
        }

        private static async Task<(int Code, string Output)?> RunAsync(string[] arguments, int timeout,
            CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = Executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            var started = false;
            try
            {
                foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
                started = process.Start();
                if (!started) return null;
                var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return (process.ExitCode, await output.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
            {
                SetAvailability(false);
                return null;
            }
            finally
            {
                if (started)
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            await process.WaitForExitAsync(CancellationToken.None)
                                .WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception
                                                      or TimeoutException)
                    {
                    }
            }
        }
    }
}
