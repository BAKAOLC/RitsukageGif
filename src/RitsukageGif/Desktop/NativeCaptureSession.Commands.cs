using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class NativeCaptureSession
    {
        internal static PortalAutomaticScrollBinding? CurrentPointerBinding()
        {
            lock (SharedLock) return _shared?.PointerBinding();
        }

        private PortalAutomaticScrollBinding? PointerBinding()
        {
            lock (_gate)
            {
                if (_capabilities is not { PointerInput: true } || _sourceGeometry is not { } geometry ||
                    !uint.TryParse(geometry.StreamId, CultureInfo.InvariantCulture, out var stream)) return null;
                return new(geometry.Generation, stream, geometry.Bounds,
                    () => IsPointerCurrent(geometry), () => ReadPointer(geometry, stream),
                    (region, token) =>
                        SendPointerCommand("prepare-pointer", geometry, ScrollCaptureAxis.Vertical, region, token),
                    (axis, region, token) => SendPointerCommand("scroll", geometry, axis, region, token),
                    () => _pointerReason ?? _sourceReason);
            }
        }

        private bool IsPointerCurrent(NativeCaptureGeometry geometry)
        {
            lock (_gate)
                return IsAvailable && _sourceReason is null && _sourceGeometry is { } current &&
                       current.Generation == geometry.Generation && current.StreamId == geometry.StreamId &&
                       current.Bounds == geometry.Bounds && _capabilities is { PointerInput: true };
        }

        private PortalPointerObservation? ReadPointer(NativeCaptureGeometry geometry, uint stream)
        {
            lock (_gate)
            {
                if (!IsPointerCurrent(geometry) || _pointer is not { Valid: true, Visible: true } pointer) return null;
                return new(geometry.Generation, stream, new((int)Math.Round(pointer.X), (int)Math.Round(pointer.Y)),
                    true);
            }
        }

        private AutomaticScrollInputResult SendPointerCommand(string command, NativeCaptureGeometry geometry,
            ScrollCaptureAxis axis, PixelRect region, CancellationToken token)
        {
            if (!IsPointerCurrent(geometry)) return AutomaticScrollInputResult.Unavailable;
            try
            {
                var reply = SendCommandAsync(command, geometry, axis, region, token: token).GetAwaiter().GetResult();
                lock (_gate) _pointerReason = reply.Reason;
                return reply.Result switch
                {
                    "sent" when command == "scroll" => AutomaticScrollInputResult.Sent,
                    "prepared" when command == "prepare-pointer" => AutomaticScrollInputResult.Sent,
                    "unavailable" => AutomaticScrollInputResult.Unavailable,
                    _ => AutomaticScrollInputResult.Failed,
                };
            }
            catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException)
            {
                lock (_gate) _pointerReason = error.Message;
                return AutomaticScrollInputResult.Failed;
            }
        }

        private async Task<NativeCaptureMetadata> SendCommandAsync(string command, NativeCaptureGeometry? geometry,
            ScrollCaptureAxis axis, PixelRect region, int? outputWidth = null, int? outputHeight = null,
            int? fps = null, bool? retainHdr = null, CancellationToken token = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            var request = Interlocked.Increment(ref _nextRequest);
            var completion =
                new TaskCompletionSource<NativeCaptureMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var buffer = new ArrayBufferWriter<byte>(256);
            await using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("command", command);
                if (fps is { } rate) writer.WriteNumber("fps", rate);
                if (retainHdr is { } hdr) writer.WriteBoolean("retainHdr", hdr);
                if (outputWidth is { } width) writer.WriteNumber("outputWidth", width);
                if (outputHeight is { } height) writer.WriteNumber("outputHeight", height);
                writer.WriteNumber("requestId", request);
                if (geometry is not null)
                {
                    writer.WriteNumber("generation", geometry.Generation);
                    writer.WriteString("streamId", geometry.StreamId);
                    if (command == "prepare-pointer")
                    {
                        writer.WriteNumber("sourceWidth", geometry.Width);
                        writer.WriteNumber("sourceHeight", geometry.Height);
                    }

                    writer.WriteString("axis", axis == ScrollCaptureAxis.Vertical ? "vertical" : "horizontal");
                    writer.WriteStartObject("region");
                    writer.WriteNumber("x", region.X);
                    writer.WriteNumber("y", region.Y);
                    writer.WriteNumber("width", region.Width);
                    writer.WriteNumber("height", region.Height);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _replies.Add(request, completion);
            }

            try
            {
                await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    await _process.StandardInput.WriteLineAsync(Encoding.UTF8.GetString(buffer.WrittenSpan).AsMemory(),
                            linked.Token)
                        .ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync(linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    _commands.Release();
                }

                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), linked.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _replies.Remove(request);
            }
        }

        internal static async Task<PortalAutomaticScrollBinding?> AuthorizePointerAsync(CancellationToken token)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            NativeCaptureSession? candidate = null;
            try
            {
                NativeCaptureSession previous;
                long version;
                NativeCaptureGeometry geometry;
                lock (SharedLock)
                {
                    previous = _shared ?? throw new CaptureSourceLostException();
                    version = _sharedVersion;
                    lock (previous._gate)
                    {
                        if (previous.PointerBinding() is { } binding) return binding;
                        geometry = previous._sourceGeometry ?? throw new CaptureSourceLostException();
                    }
                }

                candidate = new(previous.IsWindowSource ? "window" : "screen", previous._cursor, previous._fps,
                    retainHdr: previous._retainHdr, pointerInput: true);
                await candidate._first.Task.WaitAsync(TimeSpan.FromSeconds(155), token).ConfigureAwait(false);
                lock (candidate._gate)
                {
                    var next = candidate._sourceGeometry ?? throw new CaptureSourceLostException();
                    if (!geometry.StableIdentity || !next.StableIdentity || geometry.SourceId != next.SourceId ||
                        geometry.Bounds != next.Bounds)
                        throw new IOException("The authorized source changed. Select the capture region again.");
                }

                var result = candidate.PointerBinding();
                if (result is null) return null;
                CommitSelection(candidate, version, token)?.Dispose();
                candidate = null;
                return result;
            }
            finally
            {
                candidate?.Dispose();
                SelectionLock.Release();
            }
        }
    }
}
