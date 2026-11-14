using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingCameraPreviewOverlay : IAsyncDisposable
    {
        private readonly Window _owner;
        private readonly RecordingCameraSession _session;
        private readonly Action<RecordingCameraOptions> _optionsChanged;
        private readonly Window?[] _windows = new Window?[2];
        private readonly RecordingCameraPreviewControl?[] _controls = new RecordingCameraPreviewControl?[2];
        private PixelRect _region;
        private bool _visible = true;
        private bool _sourceExcludesPreview;
        private int _queued;
        private int _disposed;
        private Avalonia.PixelPoint? _dragOrigin;
        private RecordingCameraSlot? _dragSlot;
        private bool _panning;
        private RecordingCameraOptions? _notifiedOptions;

        private RecordingCameraPreviewOverlay(Window owner, RecordingCameraSession session, PixelRect region,
            Action<RecordingCameraOptions> optionsChanged, bool sourceExcludesPreview,
            LinuxCaptureExclusionProof? captureProof)
        {
            _owner = owner;
            _session = session;
            _region = region;
            _optionsChanged = optionsChanged;
            _sourceExcludesPreview = sourceExcludesPreview;
            _captureProof = captureProof;
            _session.FrameAvailable += QueueFrame;
        }


        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _session.FrameAvailable -= QueueFrame;
            if (Dispatcher.UIThread.CheckAccess()) await CloseNativeWindowsAsync();
            else await Dispatcher.UIThread.InvokeAsync(CloseNativeWindowsAsync);
        }

        internal event Action<string>? Failed;

        internal static async ValueTask<RecordingCameraPreviewOverlay> AttachAsync(Window owner,
            RecordingCameraSession session,
            PixelRect region, Action<RecordingCameraOptions> optionsChanged, bool sourceExcludesPreview = false,
            LinuxCaptureExclusionProof? captureProof = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            var overlay = new RecordingCameraPreviewOverlay(owner, session, region, optionsChanged,
                sourceExcludesPreview,
                captureProof);
            try
            {
                overlay.Refresh();
                return overlay;
            }
            catch
            {
                try
                {
                    await overlay.DisposeAsync();
                }
                catch (Exception error)
                {
                    LogHub.GetLogger("Desktop.RecordingCamera").ZLogError(error,
                        $"Camera preview cleanup failed after attachment failure.");
                }

                throw;
            }
        }

        internal void UpdateGeometry(PixelRect region, bool visible, bool sourceExcludesPreview = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            _region = region;
            _visible = visible;
            _sourceExcludesPreview = sourceExcludesPreview;
            Refresh();
        }

        private void QueueFrame()
        {
            if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _queued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _queued, 0);
                if (Volatile.Read(ref _disposed) != 0) return;
                try
                {
                    Refresh();
                }
                catch (Exception error)
                {
                    foreach (var window in _windows) window?.Hide();
                    Failed?.Invoke(error.Message);
                }
            });
        }

        private void Refresh()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (!_visible)
            {
                foreach (var window in _windows) window?.Hide();
                return;
            }

            using var snapshot = _session.CaptureSnapshot(_region.Width, _region.Height);
            for (var slot = 0; slot < _windows.Length; slot++)
            {
                var bounds = snapshot.SlotBounds[slot];
                if (bounds.IsEmpty)
                {
                    _windows[slot]?.Hide();
                    continue;
                }

                var window = _windows[slot] ??= CreateWindow(slot);
                var position = new Avalonia.PixelPoint(_region.X + (int)bounds.Left, _region.Y + (int)bounds.Top);
                var scale = (_owner.Screens.ScreenFromPoint(position) ?? _owner.Screens.Primary)?.Scaling
                            ?? _owner.RenderScaling;
                window.Position = position;
                var coordinateScale = OperatingSystem.IsMacOS() ? 1 : scale;
                window.Width = bounds.Width / coordinateScale;
                window.Height = bounds.Height / coordinateScale;
                _controls[slot]!.Update(snapshot);
                if (OperatingSystem.IsLinux() && !_sourceExcludesPreview && !PrepareNativeWindow(window)) continue;
                // Configure exclusion before showing any preview pixels.
                var excluded = RecordingRegionOverlay.Exclude(window);
                var nativeExcluded = _nativeLeases.ContainsKey(window);
                if (!_sourceExcludesPreview && !excluded && !nativeExcluded)
                    throw new NotSupportedException(
                        "The selected capture source cannot exclude camera preview windows.");
                if (!window.IsVisible) window.Show();
                if (!_sourceExcludesPreview && !nativeExcluded && !RecordingRegionOverlay.Exclude(window))
                {
                    window.Hide();
                    throw new NotSupportedException("Camera preview capture exclusion failed.");
                }
            }

            var options = _session.Options;
            if (_notifiedOptions != options)
            {
                _notifiedOptions = options;
                _optionsChanged(options);
            }
        }

        private Window CreateWindow(int slot)
        {
            var control = new RecordingCameraPreviewControl(slot);
            _controls[slot] = control;
            var window = new Window
            {
                WindowDecorations = WindowDecorations.None, ShowActivated = false, Topmost = true,
                CanResize = false, MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                WindowStartupLocation = WindowStartupLocation.Manual, Content = control,
            };
            TransientWindowShell.Configure(window);
            window.ScalingChanged += (_, _) => QueueFrame();
            window.PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
                _dragOrigin = window.PointToScreen(args.GetPosition(window));
                var options = _session.Options;
                _dragSlot = slot == 0 ? options.Primary : options.Secondary;
                _panning = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
                args.Pointer.Capture(window);
                args.Handled = true;
            };
            window.PointerMoved += (_, args) =>
            {
                if (_dragOrigin is not { } origin || _dragSlot is not { } start) return;
                var point = window.PointToScreen(args.GetPosition(window));
                var dx = point.X - origin.X;
                var dy = point.Y - origin.Y;
                var settings = _panning
                    ? start with { PanOffset = new(start.PanOffset.X - dx, start.PanOffset.Y - dy) }
                    : start with
                    {
                        Position = new PixelPoint(start.Position!.Value.X + dx, start.Position.Value.Y + dy),
                    };
                var options = _session.Options;
                _session.ApplyOptions(slot == 0
                    ? options with { Primary = settings }
                    : options with { Secondary = settings });
                Refresh();
                args.Handled = true;
            };
            window.PointerReleased += (_, args) =>
            {
                _dragOrigin = null;
                _dragSlot = null;
                args.Pointer.Capture(null);
            };
            window.PointerCaptureLost += (_, _) =>
            {
                _dragOrigin = null;
                _dragSlot = null;
            };
            return window;
        }

        private void CloseWindows()
        {
            foreach (var window in _windows) window?.Close();
            foreach (var control in _controls) control?.Dispose();
        }
    }
}
