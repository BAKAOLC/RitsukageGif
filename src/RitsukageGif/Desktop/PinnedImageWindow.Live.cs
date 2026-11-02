using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private CancellationTokenSource? _liveCancellation;
        private bool _liveConverting;
        private bool _liveInitialized;
        private WindowRoiPinSnapshot? _liveSnapshot;
        private WindowRoiPinSource? _liveSource;
        private WindowRoiCaptureState _liveState;
        private Border? _liveStatusLayer;
        private TextBlock? _liveStatusText;
        private AnimationPinSurface? _liveSurface;
        private int _liveSurfaceWidth, _liveSurfaceHeight;
        private DispatcherTimer? _liveTimer;
        private Task? _liveWork;

        internal bool LiveCaptureActive => LiveDescriptor is not null && !_closed;
        internal Func<HdrToneMapMode> LiveToneMapProvider { get; set; } = () => HdrToneMapMode.HighlightCompression;
        private bool LiveSourceReady => !LiveCaptureActive || _liveState == WindowRoiCaptureState.Active;
        private WindowRoiPinDescriptor? LiveDescriptor { get; set; }

        private WindowRoiPinFrame? SnapshotLiveFrame => _liveSnapshot?.Frame;

        private WindowRoiPinDescriptor? SnapshotLiveDescriptor =>
            LiveDescriptor is null ? null : _liveSnapshot?.Descriptor;

        internal WindowRoiPinSnapshot? CaptureLiveSnapshot()
        {
            return LiveDescriptor is null ? null : _liveSnapshot?.Retain();
        }

        internal bool MatchesLiveSource(WindowRoiPinDescriptor descriptor)
        {
            return LiveCaptureActive && LiveDescriptor!.MatchesSource(descriptor);
        }

        internal void InitializeLiveCapture(WindowRoiPinDescriptor descriptor,
            WindowRoiPinSource? preparedSource = null, WindowRoiPinFrame? initial = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!descriptor.IsValid) throw new ArgumentOutOfRangeException(nameof(descriptor));
            DisposeLiveCapture();
            LiveDescriptor = descriptor;
            _liveState = preparedSource is null ? WindowRoiCaptureState.WaitingForFrame : WindowRoiCaptureState.Active;
            _liveSource = preparedSource;
            if (initial is not null) ApplyLiveFrame(initial.Retain(), descriptor);
            OriginalContent = OriginalContent with { Hdr = null };
            PresentLiveState();
            _liveTimer ??= new() { Interval = TimeSpan.FromMilliseconds(16) };
            _liveTimer.Interval = TimeSpan.FromMilliseconds(16);
            if (!_liveInitialized)
            {
                _liveInitialized = true;
                _liveTimer.Tick += (_, _) => TickLiveCapture();
                Opened += (_, _) => ResumeLiveCapture();
                PropertyChanged += (_, args) =>
                {
                    if (args.Property != IsVisibleProperty && args.Property != WindowStateProperty) return;
                    if (IsVisible && WindowState != WindowState.Minimized) ResumeLiveCapture();
                    else PauseLiveCapture();
                };
                if (_content.ContextMenu is { } menu)
                {
                    var convert = Command("PinnedLiveConvert", () => FreezeLiveAsync(_lifetime.Token));
                    menu.Items.Insert(0, convert);
                    menu.Opened += (_, _) =>
                    {
                        convert.IsVisible = LiveCaptureActive;
                        convert.IsEnabled = LiveSourceReady;
                    };
                }
            }

            ResumeLiveCapture();
            PinLogger.ZLogInformation(
                $"Live window pin initialized. Width={descriptor.RelativeRegion.Width} Height={descriptor.RelativeRegion.Height}");
        }

        private void ResumeLiveCapture()
        {
            if (!LiveCaptureActive || _liveConverting || !IsVisible || WindowState == WindowState.Minimized ||
                _liveState == WindowRoiCaptureState.Closed) return;
            if (_liveCancellation is null || _liveCancellation.IsCancellationRequested)
            {
                if (_liveWork is not null) return;
                _liveCancellation?.Dispose();
                _liveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            }

            TickLiveCapture();
            _liveTimer?.Start();
        }

        private void PauseLiveCapture()
        {
            _liveTimer?.Stop();
            _liveCancellation?.Cancel();
            if (_liveWork is not null) return;
            _liveSource?.Dispose();
            _liveSource = null;
        }

        private void DisposeLiveCapture()
        {
            if (_closed && _liveSnapshot is not null)
                Dispatcher.UIThread.Post(ReleaseLiveSnapshot, DispatcherPriority.Background);
            PauseLiveCapture();
            if (_liveWork is not null) return;
            _liveCancellation?.Dispose();
            _liveCancellation = null;
        }

        private void TickLiveCapture()
        {
            if (!LiveCaptureActive || _liveConverting || _liveWork is not null || !IsVisible ||
                WindowState == WindowState.Minimized ||
                _liveCancellation is not { IsCancellationRequested: false } cancellation) return;
            _liveSource ??= new(LiveDescriptor!, LiveToneMapProvider);
            _liveWork = RefreshLiveCaptureAsync(_liveSource, cancellation);
        }

        private async Task RefreshLiveCaptureAsync(WindowRoiPinSource source, CancellationTokenSource cancellation)
        {
            await Task.Yield();
            var failed = false;
            WindowRoiPinFrame? pendingFrame = null;
            try
            {
                var result = await Task.Run(() => source.Capture(cancellation.Token), cancellation.Token);
                pendingFrame = result.Frame;
                if (_closed || cancellation.IsCancellationRequested || !ReferenceEquals(_liveSource, source) ||
                    result.Descriptor != LiveDescriptor) return;
                var stateChanged = _liveState != result.State;
                _liveState = result.State;
                if (pendingFrame is { } frame)
                {
                    pendingFrame = null;
                    ApplyLiveFrame(frame, result.Descriptor);
                }

                if (stateChanged)
                {
                    PinLogger.ZLogInformation($"Live window pin source state changed. State={result.State.ToString()}");
                    PresentLiveState();
                    _liveTimer!.Interval =
                        TimeSpan.FromMilliseconds(result.State == WindowRoiCaptureState.Active ? 16 : 1000);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }

                if (result.State == WindowRoiCaptureState.Closed) PauseLiveCapture();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                failed = true;
                PinLogger.ZLogError(error, $"Live window pin capture failed.");
                if (!_closed)
                {
                    _liveState = WindowRoiCaptureState.RegionUnavailable;
                    PresentLiveState(error.Message);
                    _liveTimer?.Stop();
                }

                await cancellation.CancelAsync();
            }
            finally
            {
                pendingFrame?.Dispose();
                _liveWork = null;
                if (cancellation.IsCancellationRequested || _closed || !LiveCaptureActive)
                {
                    source.Dispose();
                    if (ReferenceEquals(_liveSource, source)) _liveSource = null;
                    cancellation.Dispose();
                    if (ReferenceEquals(_liveCancellation, cancellation)) _liveCancellation = null;
                    if (!failed && !_closed && LiveCaptureActive && IsVisible &&
                        _liveState != WindowRoiCaptureState.Closed)
                        ResumeLiveCapture();
                }
            }
        }

        private void ApplyLiveFrame(WindowRoiPinFrame frame, WindowRoiPinDescriptor descriptor)
        {
            var previous = _liveSnapshot;
            _liveSnapshot = new(descriptor, frame);
            try
            {
                var resized = (int)_bitmap.Size.Width != frame.Width || (int)_bitmap.Size.Height != frame.Height;
                if (resized)
                {
                    _bitmap.Dispose();
                    _pixels = [.. frame.Pixels.AsSpan(0, checked(frame.Width * frame.Height * 4))];
                    _bitmap = new(frame.Width, frame.Height, _pixels, AlphaFormat.Unpremul);
                    _croppedImage = null;
                    if (_thumbnailRegion is { } crop)
                    {
                        var bounded = crop.Intersect(new(0, 0, frame.Width, frame.Height));
                        _thumbnailRegion = bounded.IsEmpty ? new(0, 0, frame.Width, frame.Height) : bounded;
                    }
                }

                if (_liveSurface is null || _liveSurfaceWidth != frame.Width || _liveSurfaceHeight != frame.Height)
                {
                    if (_liveSurface is { } oldSurface)
                    {
                        if (ReferenceEquals(_image.Source, oldSurface)) _image.Source = _bitmap;
                        oldSurface.Dispose();
                    }

                    _liveSurface = new(frame.Width, frame.Height);
                    _liveSurfaceWidth = frame.Width;
                    _liveSurfaceHeight = frame.Height;
                }

                frame.UpdateSurface(_liveSurface);

                if (frame.Hdr is { } hdr)
                {
                    _hdrSource = hdr;
                    if (_hdrPreview is null) ConfigureHdr(hdr);
                    else RefreshHdrPreview();
                }
                else if (_hdrPreview is not null)
                {
                    ConfigureHdr(null);
                }
                else
                {
                    _hdrSource = null;
                }

                if (resized)
                {
                    UpdateSize();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    UpdateVisibleRegion();
                }

                _image.InvalidateVisual();
            }
            finally
            {
                previous?.Dispose();
            }
        }

        private Task<ScreenshotResult>? CaptureLiveOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            if (LiveDescriptor is null) return null;
            if (!LiveSourceReady || SnapshotLiveFrame is not { } displayed)
                return Task.FromException<ScreenshotResult>(new CaptureSourceLostException());
            token.ThrowIfCancellationRequested();
            var frame = displayed.Retain();
            var region = _isThumbnail && CurrentPreferences.CopyVisibleRegion ? DisplayRegion : (PixelRect?)null;
            var scale = frame.NativePixelDensity ?? OriginalCaptureScaling;
            // ReSharper disable once MethodSupportsCancellation
            return Task.Run(() =>
            {
                using (frame)
                {
                    var png = frame.EncodePng(token);
                    var hdr = mode == OutputColorMode.Sdr ? null : frame.Hdr;
                    if (region is { } crop)
                    {
                        png = PngImageCodec.Crop(png, crop, token);
                        if (hdr is not null) hdr = CropOutputFrame(hdr, crop, token);
                    }
                    else if (hdr is not null)
                    {
                        hdr = hdr with { Pixels = hdr.Pixels.ToArray() };
                    }

                    return CreateOutput(png, hdr, mode, scale);
                }
            });
        }

        private async Task FreezeLiveAsync(CancellationToken token)
        {
            if (LiveDescriptor is null) return;
            if (!LiveSourceReady || SnapshotLiveFrame is not { } displayed) throw new CaptureSourceLostException();
            using var frame = displayed.Retain();
            _liveConverting = true;
            try
            {
                PauseLiveCapture();
                if (_liveWork is { } pending) await pending;
                DisposeLiveCapture();
                var content = await frame.PrepareContentAsync(token);
                token.ThrowIfCancellationRequested();
                if (_closed) return;
                var bitmap =
                    new ScreenshotTiledImage(content.Width, content.Height, content.Pixels, AlphaFormat.Unpremul);
                Bitmap thumbnail;
                try
                {
                    using var stream = new MemoryStream(content.ThumbnailPng, false);
                    thumbnail = new(stream);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }

                var previousBitmap = _bitmap;
                var previousThumbnail = _thumbnail;
                _bitmap = bitmap;
                _thumbnail = thumbnail;
                _pixels = content.Pixels;
                _png = content.Png;
                _sdrPng = content.SdrPng;
                ContentHash = content.ContentHash;
                _croppedImage = null;
                LiveDescriptor = null;
                if (frame.NativePixelDensity is { } density) SetOriginalCaptureScaling(density);
                OriginalContent = new(_png, _sdrPng, content.Hdr);
                ConfigureHdr(content.Hdr);
                UpdateSize();
                ReleaseLiveSnapshot();
                previousBitmap.Dispose();
                previousThumbnail.Dispose();
                PresentLiveState();
                _status.IsVisible = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _liveConverting = false;
                if (!_closed && LiveDescriptor is not null) ResumeLiveCapture();
            }
        }

        private bool CanTransformLiveImage(PinnedImageTransform transform)
        {
            return !LiveCaptureActive || (LiveSourceReady &&
                                          transform is not (PinnedImageTransform.Grayscale
                                              or PinnedImageTransform.Invert));
        }

        private async Task<bool> ApplyLiveTransformAsync(PinnedImageTransform transform, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (LiveDescriptor is not { } descriptor) return false;
            if (!CanTransformLiveImage(transform)) return true;
            var transformedDescriptor = descriptor.Transform(transform);
            LiveDescriptor = transformedDescriptor;
            _liveSource?.SetDescriptor(transformedDescriptor);
            if (_liveSnapshot is { } snapshot)
            {
                using var retained = snapshot.Frame.Retain();
                var transformed = await Task.Run(() => retained.Transform(transform, token), token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!_closed && LiveDescriptor == transformedDescriptor && ReferenceEquals(_liveSnapshot, snapshot))
                    {
                        var applied = transformed;
                        transformed = null;
                        ApplyLiveFrame(applied, transformedDescriptor);
                    }
                }
                finally
                {
                    transformed?.Dispose();
                }
            }

            TickLiveCapture();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        internal void ReleaseLiveSnapshot()
        {
            var snapshot = _liveSnapshot;
            _liveSnapshot = null;
            snapshot?.Dispose();
            if (LiveDescriptor is not null) _hdrSource = null;
            if (_liveSurface is not { } surface) return;
            if (ReferenceEquals(_image.Source, surface)) _image.Source = _bitmap;
            surface.Dispose();
            _liveSurface = null;
            _liveSurfaceWidth = _liveSurfaceHeight = 0;
        }

        private void UpdateLiveVisibleRegion(PixelRect region)
        {
            if (LiveDescriptor is null || _liveSurface is not { } surface) return;
            surface.Region = region;
            _image.Source = surface;
        }

        private bool TryRefreshLiveHdr()
        {
            if (!LiveCaptureActive) return false;
            if (!LiveSourceReady)
            {
                _hdrPreview?.SetFrame(null);
                return true;
            }

            if (_hdrPreview is null || SnapshotLiveFrame is not { Hdr: not null } displayed ||
                _annotation is not null || _selectedTextStart >= 0) return false;
            var frame = displayed.Retain();
            var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _hdrPreviewWork = work;
            _ = RefreshLiveHdrAsync(frame, DisplayRegion, work);
            return true;
        }

        private async Task RefreshLiveHdrAsync(WindowRoiPinFrame frame, PixelRect region, CancellationTokenSource work)
        {
            using (frame)
                await RefreshHdrPreviewAsync(frame.Hdr!, frame.Pixels, region, work);
        }

        private void PresentLiveState(string? error = null)
        {
            var unavailable = LiveCaptureActive && _liveState != WindowRoiCaptureState.Active;
            _image.IsVisible = !unavailable;
            if (!unavailable)
            {
                _liveStatusLayer?.IsVisible = false;
                return;
            }

            if (_liveStatusLayer is null)
            {
                _liveStatusText = new()
                {
                    FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320,
                };
                _liveStatusLayer = new()
                {
                    Background = Brushes.Black, Padding = new(12), IsHitTestVisible = false,
                    Child = new Viewbox
                    {
                        Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                        Child = _liveStatusText,
                    },
                };
                Grid.SetRowSpan(_liveStatusLayer, 2);
                _content.Children.Add(_liveStatusLayer);
                _content.SizeChanged += (_, _) =>
                    _liveStatusText.MaxWidth = Math.Max(1, _content.Bounds.Width - 24);
            }

            _liveStatusText!.Foreground = _liveState == WindowRoiCaptureState.Closed
                ? Brushes.IndianRed
                : Brushes.Orange;
            _liveStatusText.Text = _liveState == WindowRoiCaptureState.WaitingForFrame
                ? ""
                : error ??
                  UiText.Get(_liveState switch
                  {
                      WindowRoiCaptureState.Minimized => "PinnedLiveMinimized",
                      WindowRoiCaptureState.Closed => "PinnedLiveClosed",
                      _ => "PinnedLiveRegionUnavailable",
                  });
            _liveStatusLayer.IsVisible = true;
            _hdrPreviewWork?.Cancel();
            _hdrPreview?.SetFrame(null);
        }
    }
}
