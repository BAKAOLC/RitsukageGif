using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class LinuxCaptureExclusionLease : IAsyncDisposable
    {
        private const string MarkerPrefix = "RitsukageGif.CaptureOverlay.";
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.Exclusion");
        private readonly Lock _gate = new();
        private readonly Process _process;
        private readonly Task<string> _errors;
        private bool _acknowledged;
        private Task? _disposal;

        private LinuxCaptureExclusionLease(Process process)
        {
            _process = process;
            _errors = process.StandardError.ReadToEndAsync();
        }

        internal LinuxCaptureExclusionFailure ReleaseFailure { get; private set; }

        public ValueTask DisposeAsync()
        {
            lock (_gate) return new(_disposal ??= DisposeCoreAsync());
        }

        internal static string CreateWindowMarker()
        {
            return MarkerPrefix + Guid.NewGuid().ToString("N");
        }

        internal static async Task<LinuxCaptureExclusionResult> CreateAsync(LinuxCaptureExclusionProof proof,
            string windowMarker, CancellationToken token)
        {
            if (proof.Source == LinuxCaptureExclusionSource.SelectedWindow)
                return new(LinuxCaptureExclusionCapability.SelectedWindowIsolated);
            if (!OperatingSystem.IsLinux() || proof.Source != LinuxCaptureExclusionSource.KWinPortal ||
                proof.ProducerPid == 0)
                return new(LinuxCaptureExclusionCapability.Unavailable,
                    LinuxCaptureExclusionFailure.SourceDoesNotSupportExclusion);
            if (!windowMarker.StartsWith(MarkerPrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(windowMarker.AsSpan(MarkerPrefix.Length), "N", out _))
                throw new ArgumentException("The overlay requires an operation-specific native window marker.",
                    nameof(windowMarker));
            var executable = Path.Combine(AppContext.BaseDirectory, "RitsukageGif.CaptureExclusion");
            if (!File.Exists(executable))
                return new(LinuxCaptureExclusionCapability.Unavailable, LinuxCaptureExclusionFailure.MissingHelper);
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add(windowMarker);
            start.ArgumentList.Add(proof.ProducerPid.ToString(CultureInfo.InvariantCulture));
            var process = Process.Start(start) ?? throw new IOException("The capture exclusion helper did not start.");
            var lease = new LinuxCaptureExclusionLease(process);
            try
            {
                var line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) ??
                           throw new IOException("The capture exclusion helper exited without a capability reply.");
                using var reply = JsonDocument.Parse(line);
                var root = reply.RootElement;
                token.ThrowIfCancellationRequested();
                if (root.GetProperty("status").GetString() == "ready" &&
                    root.GetProperty("capability").GetString() == "kwin-portal-window-exclusion")
                {
                    lease._acknowledged = true;
                    return new(LinuxCaptureExclusionCapability.NativeCompositorExclusion, Lease: lease);
                }

                if (root.GetProperty("status").GetString() != "unavailable")
                    throw new InvalidDataException("The capture exclusion helper returned an invalid capability.");
                var reason = root.GetProperty("reason").GetString() switch
                {
                    "CompositorUnavailable" => LinuxCaptureExclusionFailure.CompositorUnavailable,
                    "ProducerChanged" => LinuxCaptureExclusionFailure.ProducerChanged,
                    "ExclusionNotAcknowledged" => LinuxCaptureExclusionFailure.ExclusionNotAcknowledged,
                    _ => throw new InvalidDataException("The capture exclusion helper returned an invalid reason."),
                };
                await lease.DisposeAsync().ConfigureAwait(false);
                return new(LinuxCaptureExclusionCapability.Unavailable, reason);
            }
            catch
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch (IOException error)
                {
                    Logger.ZLogDebug(error, $"Capture exclusion helper input was already closed.");
                }

                await _process.WaitForExitAsync().ConfigureAwait(false);
                if (_acknowledged)
                {
                    var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        ReleaseFailure = LinuxCaptureExclusionFailure.ReleaseNotAcknowledged;
                    }
                    else
                    {
                        using var reply = JsonDocument.Parse(line);
                        var root = reply.RootElement;
                        if (root.GetProperty("status").GetString() != "released" ||
                            !root.GetProperty("restored").GetBoolean() || !root.GetProperty("unloaded").GetBoolean())
                            ReleaseFailure = LinuxCaptureExclusionFailure.ReleaseNotAcknowledged;
                    }
                }

                var diagnostic = await _errors.ConfigureAwait(false);
                if (_process.ExitCode != 0 || ReleaseFailure != LinuxCaptureExclusionFailure.None ||
                    !string.IsNullOrWhiteSpace(diagnostic))
                    Logger.ZLogWarning(
                        $"Capture exclusion helper ended. ExitCode={_process.ExitCode} ReleaseFailure={ReleaseFailure} Detail={diagnostic}");
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
