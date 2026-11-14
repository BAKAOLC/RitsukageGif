using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingCameraSession : IAsyncDisposable
    {
        private readonly RecordingCameraSource?[] _sources = new RecordingCameraSource?[2];

        private readonly (RecordingCameraSlot? Settings, SKRect Bounds)[] _layouts =
            new (RecordingCameraSlot?, SKRect)[2];

        private readonly Lock _lifetime = new();
        private readonly Lock _layoutGate = new();
        private int _disposed;
        private Task _disposal = Task.CompletedTask;
        private RecordingCameraOptions _options;
        private readonly Lock _sourceGate = new();
        private readonly SemaphoreSlim _configureGate = new(1, 1);
        private readonly CancellationTokenSource _shutdown = new();
        private int _generation;

        private RecordingCameraSession(RecordingCameraOptions options)
        {
            _options = options;
        }

        internal RecordingCameraOptions Options => Volatile.Read(ref _options);

        public ValueTask DisposeAsync()
        {
            lock (_lifetime)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) _disposal = Task.Run(DisposeCoreAsync);
                return new(_disposal);
            }
        }

        internal event Action? FrameAvailable;

        internal static async Task<RecordingCameraSession> CreateAsync(RecordingCameraOptions options,
            CancellationToken token = default)
        {
            var session = new RecordingCameraSession(options);
            try
            {
                await session.ConfigureAsync(options, token).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal void ApplyOptions(RecordingCameraOptions options)
        {
            lock (_sourceGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                var previous = Options;
                if (options.Enabled != previous.Enabled ||
                    !SameDevice(options.Primary.Device, previous.Primary.Device) ||
                    !SameDevice(options.Secondary.Device, previous.Secondary.Device))
                    throw new InvalidOperationException("Camera device changes require asynchronous configuration.");
                Volatile.Write(ref _options, options);
            }

            FrameAvailable?.Invoke();
        }

        internal async Task ConfigureAsync(RecordingCameraOptions options, CancellationToken token = default)
        {
            using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
            await _configureGate.WaitAsync(preparation.Token).ConfigureAwait(false);
            var opened = new List<RecordingCameraSource>();
            try
            {
                RecordingCameraSource?[] previous;
                RecordingCameraOptions before;
                lock (_sourceGate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    previous = [.. _sources];
                    before = Options;
                }

                switch (options)
                {
                    case { Enabled: true, Primary.Device: null, Secondary.Device: null }:
                        throw new InvalidOperationException("No camera device is selected.");
                    case { Enabled: true, Primary.Device: { } primary }
                        when SameDevice(primary, options.Secondary.Device):
                        throw new InvalidOperationException("Select different camera devices for the two slots.");
                }

                var next = new RecordingCameraSource?[2];
                for (var slot = 0; slot < next.Length; slot++)
                {
                    var device = slot == 0 ? options.Primary.Device : options.Secondary.Device;
                    if (!options.Enabled || device is null) continue;
                    for (var oldSlot = 0; oldSlot < previous.Length; oldSlot++)
                    {
                        var oldDevice = oldSlot == 0 ? before.Primary.Device : before.Secondary.Device;
                        if (SameDevice(device, oldDevice) && previous[oldSlot] is { } existing)
                        {
                            next[slot] = existing;
                            break;
                        }
                    }

                    if (next[slot] is not null) continue;
                    next[slot] = await RecordingCameraSource.CreateAsync(device, _shutdown.Token, preparation.Token)
                        .ConfigureAwait(false);
                    opened.Add(next[slot]!);
                }

                preparation.Token.ThrowIfCancellationRequested();
                lock (_sourceGate)
                lock (_layoutGate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    _generation++;
                    for (var slot = 0; slot < next.Length; slot++)
                    {
                        if (!ReferenceEquals(_sources[slot], next[slot])) _layouts[slot] = default;
                        _sources[slot] = next[slot];
                    }

                    Volatile.Write(ref _options, options);
                    foreach (var source in opened) source.FrameAvailable += OnFrameAvailable;
                    opened.Clear();
                }

                FrameAvailable?.Invoke();
                foreach (var source in previous)
                    if (source is not null && !next.Contains(source))
                    {
                        source.FrameAvailable -= OnFrameAvailable;
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
            }
            finally
            {
                try
                {
                    foreach (var source in opened) await source.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _configureGate.Release();
                }
            }
        }

        private static bool SameDevice(RecordingCameraDevice? left, RecordingCameraDevice? right)
        {
            return left is null
                ? right is null
                : right is not null && left.Id == right.Id && left.InputFormat == right.InputFormat;
        }

        internal CapturedFrameSet Composite(CapturedFrameSet source, PixelRect capturePhysicalRegion,
            CancellationToken token = default)
        {
            using var snapshot = CaptureSnapshot(capturePhysicalRegion.Width, capturePhysicalRegion.Height);
            var scaleX = (float)source.Sdr.Width / capturePhysicalRegion.Width;
            var scaleY = (float)source.Sdr.Height / capturePhysicalRegion.Height;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new(0, 0, source.Sdr.Width, source.Sdr.Height));
            canvas.Scale(scaleX, scaleY);
            canvas.DrawPicture(snapshot.Picture);
            using var picture = recorder.EndRecording();
            var area = snapshot.AffectedArea;
            area = new(area.Left * scaleX, area.Top * scaleY, area.Right * scaleX, area.Bottom * scaleY);
            return RecordingOverlayComposition.Composite(source, picture, area, token);
        }

        internal RecordingCameraSnapshot CaptureSnapshot(int captureWidth, int captureHeight)
        {
            RecordingCameraOptions options;
            int generation;
            PreviewFrame? primary = null;
            PreviewFrame? secondary = null;
            try
            {
                lock (_sourceGate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    options = Options;
                    generation = _generation;
                    if (options.Enabled)
                    {
                        primary = _sources[0]?.Read();
                        secondary = _sources[1]?.Read();
                    }
                }
            }
            catch
            {
                primary?.Dispose();
                throw;
            }

            using var primaryFrame = primary;
            using var secondaryFrame = secondary;
            var pictures = new SKPicture?[2];
            var bounds = new SKRect[2];
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new(0, 0, captureWidth, captureHeight));
            try
            {
                for (var slot = 0; slot < _sources.Length; slot++)
                {
                    if (!options.Enabled) break;
                    var camera = slot == 0 ? primary : secondary;
                    if (camera is null) continue;
                    var settings = slot == 0 ? options.Primary : options.Secondary;
                    bounds[slot] = ResolveBounds(slot, settings, camera.Width, camera.Height, captureWidth,
                        captureHeight,
                        generation);
                    using var slotRecorder = new SKPictureRecorder();
                    var drawing = slotRecorder.BeginRecording(new(0, 0, captureWidth, captureHeight));
                    DrawCamera(drawing, camera, settings, bounds[slot]);
                    pictures[slot] = slotRecorder.EndRecording();
                    canvas.DrawPicture(pictures[slot]!);
                }

                return new(recorder.EndRecording(), pictures, bounds);
            }
            catch
            {
                foreach (var picture in pictures) picture?.Dispose();
                throw;
            }
        }

        private void OnFrameAvailable()
        {
            FrameAvailable?.Invoke();
        }

        internal int GetSizeLimit(int captureWidth, int captureHeight)
        {
            lock (_sourceGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                var options = Options;
                var limit = 480;
                for (var slot = 0; slot < _sources.Length; slot++)
                {
                    if (_sources[slot]?.ReadFrameSize() is not { } size) continue;
                    var settings = slot == 0 ? options.Primary : options.Secondary;
                    var bounds = RecordingCameraLayout.Bounds(settings with { Size = 480 }, size.Width, size.Height,
                        captureWidth, captureHeight, false);
                    var slotLimit = (int)(settings.Rotation % 180 == 0 ? bounds.Height : bounds.Width) -
                                    settings.BorderWidth * 2;
                    limit = Math.Min(limit, slotLimit);
                }

                return Math.Max(1, limit);
            }
        }

        private async Task DisposeCoreAsync()
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            await _configureGate.WaitAsync().ConfigureAwait(false);
            try
            {
                RecordingCameraSource?[] sources;
                lock (_sourceGate)
                {
                    sources = [.. _sources];
                    Array.Clear(_sources);
                }

                foreach (var source in sources)
                    if (source is not null)
                    {
                        source.FrameAvailable -= OnFrameAvailable;
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
            }
            finally
            {
                _configureGate.Release();
                _shutdown.Dispose();
            }
        }

        private SKRect ResolveBounds(int slot, RecordingCameraSlot settings, int sourceWidth, int sourceHeight,
            int captureWidth, int captureHeight, int generation)
        {
            lock (_layoutGate)
            {
                if (generation != Volatile.Read(ref _generation))
                    return RecordingCameraLayout.Bounds(settings, sourceWidth, sourceHeight, captureWidth,
                        captureHeight, false);
                var previous = _layouts[slot];
                var bounds = RecordingCameraLayout.Bounds(settings, sourceWidth, sourceHeight,
                    captureWidth, captureHeight, previous.Settings is null);
                if (previous.Settings is { } earlier && settings.Position == earlier.Position &&
                    (!bounds.Width.Equals(previous.Bounds.Width) || !bounds.Height.Equals(previous.Bounds.Height)))
                {
                    var placement = settings with
                    {
                        Position = new((int)Math.Round(previous.Bounds.MidX - bounds.Width / 2,
                                MidpointRounding.AwayFromZero),
                            (int)Math.Round(previous.Bounds.MidY - bounds.Height / 2, MidpointRounding.AwayFromZero)),
                    };
                    bounds = RecordingCameraLayout.Bounds(placement, sourceWidth, sourceHeight,
                        captureWidth, captureHeight, false);
                }

                var unrotatedHeight = settings.Rotation % 180 == 0 ? bounds.Height : bounds.Width;
                var size = Math.Max(1, (int)unrotatedHeight - settings.BorderWidth * 2);
                var normalized = settings with { Size = size, Position = new((int)bounds.Left, (int)bounds.Top) };
                _layouts[slot] = (normalized, bounds);
                if (normalized != settings)
                    while (true)
                    {
                        var current = Options;
                        if ((slot == 0 ? current.Primary : current.Secondary) != settings) break;
                        var updated = slot == 0
                            ? current with { Primary = normalized }
                            : current with { Secondary = normalized };
                        if (ReferenceEquals(Interlocked.CompareExchange(ref _options, updated, current), current))
                            break;
                    }

                return bounds;
            }
        }

        private static void DrawCamera(SKCanvas canvas, PreviewFrame camera, RecordingCameraSlot settings,
            SKRect outer)
        {
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.Color = settings.BorderColor.WithAlpha(255);
            using var outerPath = new SKPath();
            if (settings.Shape == RecordingCameraShape.Circle) outerPath.AddOval(outer);
            else outerPath.AddRect(outer);
            var inner = new SKRect(outer.Left + settings.BorderWidth, outer.Top + settings.BorderWidth,
                outer.Right - settings.BorderWidth, outer.Bottom - settings.BorderWidth);
            using var clip = new SKPath();
            if (settings.Shape == RecordingCameraShape.Circle) clip.AddOval(inner);
            else clip.AddRect(inner);
            canvas.Save();
            try
            {
                canvas.ClipPath(clip, antialias: true);
                var rotated = settings.Rotation % 180 != 0;
                var width = rotated ? camera.Height : camera.Width;
                var height = rotated ? camera.Width : camera.Height;
                var scale = Math.Max(inner.Width / width, inner.Height / height);
                var horizontalSlack = Math.Max(0, (width * scale - inner.Width) / 2);
                var verticalSlack = Math.Max(0, (height * scale - inner.Height) / 2);
                var panX = Math.Clamp(settings.PanOffset.X, -horizontalSlack, horizontalSlack);
                var panY = Math.Clamp(settings.PanOffset.Y, -verticalSlack, verticalSlack);
                canvas.Translate(inner.MidX - panX, inner.MidY - panY);
                canvas.Scale(scale * (settings.Mirrored ? -1 : 1), scale);
                canvas.RotateDegrees(settings.Rotation);
                canvas.Translate(-camera.Width / 2f, -camera.Height / 2f);
                using var image = SKImage.FromPixelCopy(
                    new(camera.Width, camera.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                    camera.Pixels.AsSpan(0, checked(camera.Width * camera.Height * 4)), camera.Width * 4);
                paint.Color = SKColors.White;
                canvas.DrawImage(image, new SKRect(0, 0, camera.Width, camera.Height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            }
            finally
            {
                canvas.Restore();
            }

            if (settings.BorderWidth > 0)
            {
                using var border = outerPath.Op(clip, SKPathOp.Difference);
                paint.Color = settings.BorderColor.WithAlpha(255);
                canvas.DrawPath(border, paint);
            }
        }
    }
}
