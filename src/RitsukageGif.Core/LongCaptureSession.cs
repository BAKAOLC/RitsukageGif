using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class LongCaptureSession : ILongCaptureSession
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.LongCapture");
        private readonly IStaticImageService _imageService;
        private readonly Lock _disposeLock = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly SemaphoreSlim _operationGate = new(1);
        private readonly LongCaptureOptions _options;
        private int _disposeRequested;
        private Task? _disposeTask;
        private bool _disposed;
        private string? _error;
        private ScrollCaptureResult? _lastResult;
        private PixelRect _region;
        private Task? _run;
        private CancellationTokenSource? _runCancellation;
        private bool _running;
        private ScrollCaptureSession? _session;
        private ILongCaptureFrameSource? _source;
        private bool _stopped;

        public LongCaptureSession(LongCaptureOptions options, ILongCaptureFrameSource? source = null,
            IStaticImageService? imageService = null)
        {
            ValidateOptions(options);
            if (options.Source == LongCaptureSourceKind.Frames != source is null)
                throw new ArgumentException("Screen and window captures require a frame source; frame feeds do not.",
                    nameof(source));
            _options = options;
            _lifetimeToken = _lifetime.Token;
            _region = options.Region;
            _imageService = imageService ?? new StaticImageService();
            _session = new(options.Region.Width, options.Region.Height, options.Stitching);
            _source = source;
        }

        public async Task<ScrollCaptureResult> CaptureAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                EnsureWritable();
                return await CaptureCoreAsync(cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<ScrollCaptureResult> FeedAsync(ReadOnlyMemory<byte> encodedImage,
            CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            var operationToken = cancellation.Token;
            await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
            try
            {
                EnsureWritable();
                if (encodedImage.IsEmpty)
                    throw new ArgumentException("The encoded image is empty.", nameof(encodedImage));
                var result = await Task.Run(() => DecodeAndAppend(encodedImage, operationToken),
                    operationToken).ConfigureAwait(false);
                _lastResult = result;
                return result;
            }
            catch (MagickException) when (operationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(operationToken);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<ScrollCaptureResult> FeedFramesAsync(CapturedFrameSet frames,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                EnsureWritable();
                _lastResult = _session!.TryAppend(frames, _region.X, _region.Y, cancellation.Token);
                return _lastResult;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task MoveAsync(int position, CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                EnsureWritable();
                _region = _options.Stitching.Axis == ScrollCaptureAxis.Vertical
                    ? _region with { Y = position }
                    : _region with { X = position };
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<LongCaptureState> GetStateAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                EnsureAlive();
                return new(_running, _stopped, _region, _options.Stitching.Axis, _session!.Width, _session.Height,
                    _lastResult, _error);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<ScrollCaptureImage> ReadAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            var operationToken = cancellation.Token;
            await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
            try
            {
                EnsureAlive();
                return await Task.Run(() => _session!.ExportPixels(operationToken), operationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<MediaInfo> SaveAsync(StaticCaptureOutput output, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (output.Color is not (OutputColorMode.Sdr or OutputColorMode.Hdr))
                throw new NotSupportedException("Long capture output requires one SDR or HDR destination.");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            var operationToken = cancellation.Token;
            await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
            try
            {
                EnsureAlive();
                if (output.Color == OutputColorMode.Hdr && !_options.Stitching.RetainHdr)
                    throw new InvalidOperationException("The long capture session did not retain HDR pixels.");
                var image = await Task.Run(() => _session!.ExportPixels(operationToken), operationToken)
                    .ConfigureAwait(false);
                return output.Color == OutputColorMode.Hdr
                    ? await HdrImageService.SaveAsync(image.Hdr!, output.Path, output.HdrImage, operationToken)
                        .ConfigureAwait(false)
                    : await _imageService.SavePixelsAsync(image.Pixels, image.Width, image.Height, output.Path,
                        output.Image, operationToken).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        internal static void ValidateOptions(LongCaptureOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var stitching = options.Stitching;
            if (options.Region.IsEmpty || !Enum.IsDefined(options.Source) || !Enum.IsDefined(options.ToneMap) ||
                !double.IsFinite(options.IntervalSeconds) || options.IntervalSeconds <= 0 ||
                options.IntervalSeconds > (uint.MaxValue - 1) / 1000d ||
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                stitching is null ||
                !Enum.IsDefined(stitching.Axis) || stitching.LeadingInset < 0 || stitching.TrailingInset < 0 ||
                !double.IsFinite(stitching.MinimumOverlap) || stitching.MinimumOverlap is < 0.2 or > 0.8 ||
                stitching.MaximumLength < 1 || stitching.MaximumBytes < 1)
                throw new ArgumentException("Invalid long capture options.", nameof(options));
            var vertical = stitching.Axis == ScrollCaptureAxis.Vertical;
            var axisLength = (long)(vertical ? options.Region.Height : options.Region.Width) -
                             stitching.LeadingInset - stitching.TrailingInset;
            var crossLength = vertical ? options.Region.Width : options.Region.Height;
            if (axisLength < 16 || crossLength < 8 || axisLength > stitching.MaximumLength)
                throw new ArgumentException("The capture region exceeds the stitching size limits.", nameof(options));
        }

        private async Task<ScrollCaptureResult> CaptureCoreAsync(CancellationToken token)
        {
            var source = _source ?? throw new NotSupportedException("This session has no live capture source.");
            var frames = await source.CaptureAsync(_region, _options.Stitching.RetainHdr, token).ConfigureAwait(false);
            _lastResult = _session!.TryAppend(frames, _region.X, _region.Y, token);
            return _lastResult;
        }

        private ScrollCaptureResult DecodeAndAppend(ReadOnlyMemory<byte> encodedImage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (HdrImageDecoder.TryDecode(encodedImage, _options.Stitching.RetainHdr,
                    (_region.Width, _region.Height), token) is { } hdr)
            {
                var sdr = HdrToneMapping.Map(hdr, _options.ToneMap, token);
                return _session!.TryAppend(new CapturedFrameSet(sdr, hdr, 0), _region.X, _region.Y, token);
            }

            if (_options.Stitching.RetainHdr)
                throw new NotSupportedException(
                    "The encoded image does not provide a supported high precision capture plane.");
            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            image.Ping(encodedImage.Span, settings);
            var swapped = (int)image.Orientation is >= 5 and <= 8;
            if ((swapped ? image.Height : image.Width) != _region.Width ||
                (swapped ? image.Width : image.Height) != _region.Height)
                throw new ArgumentException("The frame dimensions must match the long capture region.");
            token.ThrowIfCancellationRequested();
            image.Read(encodedImage.Span, settings);
            image.AutoOrient();
            if (image.GetColorProfile() is not null && !image.TransformColorSpace(ColorProfiles.SRGB))
                throw new InvalidDataException();
            image.ColorSpace = ColorSpace.sRGB;
            ValidateImageSize(image);
            using var pixels = image.GetPixels();
            var bgra = pixels.ToByteArray(PixelMapping.BGRA)!;
            return _session!.TryAppend(bgra, _region.X, _region.Y, token);
        }

        private void ValidateImageSize(MagickImage image)
        {
            if (image.Width != _region.Width || image.Height != _region.Height)
                throw new ArgumentException("The frame dimensions must match the long capture region.");
        }

        private void EnsureAlive()
        {
            ObjectDisposedException.ThrowIf(_disposed || Volatile.Read(ref _disposeRequested) != 0, this);
        }

        private void EnsureWritable()
        {
            EnsureAlive();
            if (_stopped) throw new InvalidOperationException("The long capture session has stopped.");
        }
    }
}
