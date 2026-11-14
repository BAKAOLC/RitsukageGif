using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingSourceViewport : Window, IAsyncDisposable
    {
        private readonly NativeCaptureSession _source;
        private readonly NativeCaptureDemand _demand;
        private readonly RecordingAnnotationLayer _annotations;
        private readonly ImageViewport _viewport = new() { PanEnabled = false };
        private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Grid _content = new();
        private readonly Border _frame = new() { BorderThickness = new(1), IsHitTestVisible = false, ZIndex = 1 };
        private readonly Lock _refreshGate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private ScreenshotAnnotationSurface _surface;
        private PixelRect _region;
        private RecordingCameraSession? _camera;
        private WriteableBitmap? _bitmap;
        private Task _refresh = Task.CompletedTask;
        private Task? _disposal;
        private LinuxCaptureExclusionLease? _exclusion;
        private bool _disposed;
        private bool _ready;
        private long _refreshedAt;

        internal ScreenshotRecordingSourceViewport(NativeCaptureSession source, RecordingAnnotationLayer annotations,
            PixelRect region)
        {
            _source = source;
            _demand = new(source.Retain());
            _annotations = annotations;
            _region = region;
            _surface = annotations.CreateHostedSurface(this, region);
            _content.Children.Add(_image);
            _content.Children.Add(_surface);
            _content.Children.Add(_frame);
            _viewport.ImageContent = _content;
            _viewport.PixelSize = new(region.Width, region.Height);
            _viewport.ViewChanged += (_, _) => _surface.ControlScale = RenderScaling / _viewport.Zoom;
            InitializeCameraInput();
            Title = UiText.Get("RecorderTitle");
            Width = Math.Clamp(region.Width, 320, 960);
            Height = Math.Clamp(region.Height, 200, 680);
            MinWidth = 320;
            MinHeight = 200;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            TransientWindowShell.Configure(this);
            _source.FrameAvailable += QueueFrame;
            PositionChanged += (_, _) => PlacementChanged?.Invoke();
            SizeChanged += (_, _) => PlacementChanged?.Invoke();
            Closing += (_, args) =>
            {
                if (_disposed) return;
                args.Cancel = true;
                CloseRequested?.Invoke();
            };
        }

        public ValueTask DisposeAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            return new(_disposal ??= DisposeCoreAsync());
        }

        internal event Action? PlacementChanged;
        internal event Action? CloseRequested;
        internal event Action<Exception>? Failed;
        internal event Action<RecordingCameraOptions>? CameraOptionsChanged;

        internal void SetFrameBrush(IBrush brush)
        {
            _frame.BorderBrush = brush;
        }

        internal void SetPreviewVisible(bool visible)
        {
            if (_disposed || !_ready) return;
            if (visible)
            {
                if (!IsVisible) Show();
                QueueFrame();
            }
            else
            {
                Hide();
            }
        }

        internal async Task<LinuxCaptureExclusionResult> ShowSourceAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (proof.Source != LinuxCaptureExclusionSource.SelectedWindow &&
                proof.Source != LinuxCaptureExclusionSource.KWinPortal)
                return new(LinuxCaptureExclusionCapability.Unavailable,
                    LinuxCaptureExclusionFailure.SourceDoesNotSupportExclusion);
            var marker = LinuxCaptureExclusionLease.CreateWindowMarker();
            Title = marker;
            Opacity = 0;
            Show();
            var result = await LinuxCaptureExclusionLease.CreateAsync(proof, marker, token);
            _exclusion = result.Lease;
            token.ThrowIfCancellationRequested();
            if (_disposed || result.Capability == LinuxCaptureExclusionCapability.Unavailable)
            {
                Hide();
                return result;
            }

            Title = UiText.Get("RecorderTitle");
            Content = _viewport;
            Opacity = 1;
            _ready = true;
            _viewport.FitToViewport();
            QueueFrame();
            return result;
        }

        internal async Task SetCameraAsync(RecordingCameraSession? camera)
        {
            if (ReferenceEquals(_camera, camera)) return;
            if (_camera is { } previous) previous.FrameAvailable -= QueueFrame;
            Task pending;
            lock (_refreshGate)
            {
                _camera = camera;
                pending = _refresh;
            }

            await pending;
            if (camera is not null) camera.FrameAvailable += QueueFrame;
            QueueFrame();
        }

        internal void UpdateRegion(PixelRect region)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_region == region) return;
            lock (_refreshGate) _region = region;
            _content.Children.Remove(_surface);
            _surface.Dispose();
            _surface = _annotations.CreateHostedSurface(this, region);
            _content.Children.Add(_surface);
            _viewport.PixelSize = new(region.Width, region.Height);
            QueueFrame();
        }

        private async Task DisposeCoreAsync()
        {
            Task pending;
            lock (_refreshGate)
            {
                _disposed = true;
                pending = _refresh;
            }

            _source.FrameAvailable -= QueueFrame;
            if (_camera is { } camera) camera.FrameAvailable -= QueueFrame;
            Hide();
            await _lifetime.CancelAsync();
            await pending;
            _annotations.DetachHostedSurface(this);
            _surface.Dispose();
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _demand.Dispose();
            try
            {
                if (_exclusion is { } lease) await lease.DisposeAsync();
            }
            finally
            {
                _lifetime.Dispose();
                Close();
            }
        }
    }
}
