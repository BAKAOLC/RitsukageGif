using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        private readonly Dictionary<long, (PixelRect? Region, double? Density)> _demands = [];
        private readonly SemaphoreSlim _configurationGate = new(1, 1);
        private TaskCompletionSource _nextFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _nextDemand, _configurationId;
        private bool _suspended;
        private PixelRect? _configuredRegion;
        private int _configuredWidth, _configuredHeight;

        internal long RegisterDemand()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var id = ++_nextDemand;
                _demands.Add(id, (null, null));
                return id;
            }
        }

        internal void RemoveDemand(long id)
        {
            bool idle;
            lock (_gate)
            {
                _demands.Remove(id);
                idle = _demands.Count == 0 && !_disposed;
            }

            if (idle) _ = SuspendIdleAsync();
        }

        internal void ConfigureDemand(long id, PixelRect? region, double? density, CancellationToken token)
        {
            AwaitFirstFrame(token);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            _configurationGate.Wait(linked.Token);
            try
            {
                bool wasSuspended;
                lock (_gate) wasSuspended = _suspended;
                if (wasSuspended)
                    SendCommandAsync("capabilities", null, default, default, token: linked.Token).GetAwaiter()
                        .GetResult();
                PixelRect requested;
                NativeCaptureGeometry source;
                int width, height;
                bool configure, resume;
                long expected;
                ulong before;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    ObjectDisposedException.ThrowIf(!_demands.ContainsKey(id), typeof(NativeCaptureDemand));
                    _demands[id] = (region, density);
                    if (_sourceReason is not null) throw new IOException(_sourceReason);
                    source = _sourceGeometry ?? throw new CaptureSourceLostException();
                    var full = source.Bounds;
                    var areas = _demands.Values.Select(value => (Region: (value.Region ?? full).Intersect(full),
                        Density: value.Density ?? source.BackingScale)).Where(value => !value.Region.IsEmpty).ToArray();
                    if (areas.Length == 0 || (region is { } wanted && wanted.Intersect(full).IsEmpty))
                        throw new CaptureSourceLostException();
                    var left = areas.Min(value => value.Region.X);
                    var top = areas.Min(value => value.Region.Y);
                    var right = areas.Max(value => value.Region.Right);
                    var bottom = areas.Max(value => value.Region.Bottom);
                    requested = new(left, top, right - left, bottom - top);
                    var outputDensity = areas.Max(value => value.Density);
                    width = Math.Max(1, checked((int)Math.Ceiling(requested.Width * outputDensity)));
                    height = Math.Max(1, checked((int)Math.Ceiling(requested.Height * outputDensity)));
                    configure = !((_configuredRegion == requested && _configuredWidth == width &&
                                   _configuredHeight == height) ||
                                  (_geometry?.Bounds == requested && _geometry.PixelWidth == width &&
                                   _geometry.PixelHeight == height));
                    resume = _suspended;
                    expected = _configurationId;
                    before = _frameSequence;
                    if (!configure && !resume) return;
                }

                if (configure)
                {
                    var reply = SendCommandAsync("configure", source, default, requested, width, height, linked.Token)
                        .GetAwaiter().GetResult();
                    if (reply.Result != "configured")
                        throw new CaptureSourceLostException(
                            new IOException(reply.Reason ?? "The source region is unavailable."));
                    expected = reply.RequestId;
                    Logger.ZLogDebug(
                        $"Native capture region configured. Generation={_generation} ConfigurationId={expected} Region={requested} Width={width} Height={height}");
                }

                if (resume)
                {
                    var reply = SendCommandAsync("resume", null, default, default, token: linked.Token).GetAwaiter()
                        .GetResult();
                    if (reply.Result != "resumed")
                        throw new IOException(reply.Reason ?? "The capture source could not resume.");
                    lock (_gate) _suspended = false;
                    Logger.ZLogDebug($"Native capture resumed. Generation={_generation}");
                }

                var started = Stopwatch.GetTimestamp();
                while (true)
                {
                    Task arrived;
                    lock (_gate)
                    {
                        if (_sourceReason is not null) throw new IOException(_sourceReason);
                        if (_configurationId == expected && (!resume || _frameSequence > before))
                        {
                            _configuredRegion = requested;
                            _configuredWidth = width;
                            _configuredHeight = height;
                            return;
                        }

                        if (_failure is { } failure) ExceptionDispatchInfo.Throw(failure);
                        arrived = _nextFrame.Task;
                    }

                    var remaining = TimeSpan.FromSeconds(3) - Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero)
                        throw new TimeoutException("The source did not publish the configured capture region.");
                    arrived.WaitAsync(remaining, linked.Token).GetAwaiter().GetResult();
                }
            }
            finally
            {
                _configurationGate.Release();
            }
        }

        private async Task SuspendWhenIdleAsync(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            await _configurationGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                    if (_demands.Count != 0 || _disposed || _suspended)
                        return;
                var reply = await SendCommandAsync("suspend", null, default, default, token: linked.Token)
                    .ConfigureAwait(false);
                if (reply.Result != "suspended")
                    throw new IOException(reply.Reason ?? "The capture source could not suspend.");
                lock (_gate) _suspended = true;
                Logger.ZLogDebug($"Native capture suspended. Generation={_generation}");
            }
            finally
            {
                _configurationGate.Release();
            }
        }

        private async Task SuspendIdleAsync()
        {
            try
            {
                await SuspendWhenIdleAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException)
            {
                lock (_gate)
                    if (!_disposed)
                        _sourceReason = "Capture suspension failed: " + error.Message;
            }
        }

        internal static IReadOnlyList<WindowTarget> AuthorizedWindows()
        {
            lock (SharedLock)
            {
                if (_shared is not { _window: true, IsAvailable: true } session) return [];
                lock (session._gate)
                {
                    var source = session._sourceGeometry;
                    if (source is null) return [];
                    return
                    [
                        new(-1, "", "", source.Bounds, source.Bounds)
                        {
                            NativeSourceIdentity = SourceIdentity(source),
                            NativeAuthorization = new(session._authorizationId, SourceIdentity(source),
                                source.Generation),
                        },
                    ];
                }
            }
        }

        private static string SourceIdentity(NativeCaptureGeometry source)
        {
            return source is { StableIdentity: true, SourceId: { } id }
                ? id
                : source.Generation.ToString(CultureInfo.InvariantCulture) + "/" + source.StreamId;
        }

        internal static NativeCaptureSession CurrentAuthorizedWindow(NativeCaptureAuthorization? expected = null)
        {
            var session = CurrentShared();
            if (!session._window) throw new NotSupportedException(UiText.Get("PortalWindowMatchingUnavailable"));
            if (expected is null) return session;
            lock (session._gate)
            {
                var source = session._sourceGeometry;
                if (source is null || session._authorizationId != expected.AuthorizationId ||
                    source.Generation != expected.Generation || SourceIdentity(source) != expected.SourceIdentity)
                    throw new CaptureSourceLostException();
            }

            return session;
        }

        internal static NativeCaptureSession CurrentShared()
        {
            lock (SharedLock) return _shared ?? throw new CaptureSourceLostException();
        }
    }
}
