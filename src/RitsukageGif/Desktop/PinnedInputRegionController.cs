using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedInputRegionController : IDisposable
    {
        private readonly Window _window;
        private readonly Func<bool> _enabled;
        private readonly Func<PixelRect?> _viewportBounds;
        private readonly Action _refreshHdrGeometry;
        private readonly IPinnedInputRegionBackend _backend;
        private readonly Action<Exception> _failed;
        private readonly PinnedInputUiCoverage _renderer = new();
        private readonly HashSet<Visual> _observed = [];
        private readonly HashSet<Visual> _tree = [];
        private PinnedInputCoverage? _ui;
        private PreviewFrame? _hdr;
        private long _uiVersion, _renderedUiVersion = -1;
        private double _renderScaling;
        private bool _queued, _disposed;
        private long _hdrVersion, _processedUiVersion = -1, _processedHdrVersion = -1;
        private PixelRect? _processedViewport;
        private bool _regionApplied;

        internal PinnedInputRegionController(Window window, Func<bool> enabled, Func<PixelRect?> viewportBounds,
            Action refreshHdrGeometry, IPinnedInputRegionBackend backend, Action<Exception> failed)
        {
            _window = window;
            _enabled = enabled;
            _viewportBounds = viewportBounds;
            _refreshHdrGeometry = refreshHdrGeometry;
            _backend = backend;
            _failed = failed;
            _window.Opened += OnLayout;
            _window.LayoutUpdated += OnLayout;
            _window.Closed += OnClosed;
            ObserveTree();
            InvalidateUi();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Opened -= OnLayout;
            _window.LayoutUpdated -= OnLayout;
            _window.Closed -= OnClosed;
            foreach (var visual in _observed)
            {
                visual.PropertyChanged -= OnVisualPropertyChanged;
                if (visual is IPinnedInputCoverageSource source) source.InputCoverageChanged -= InvalidateUi;
            }

            _observed.Clear();
            _hdr?.Dispose();
            _hdr = null;
            _renderer.Dispose();
            try
            {
                _backend.Dispose();
            }
            catch (Exception error)
            {
                _failed(error);
            }
        }

        internal void SetHdrFrame(PreviewFrame? frame)
        {
            var previous = _hdr;
            _hdr = frame?.HdrPixels is not null && _enabled() ? frame.Retain() : null;
            previous?.Dispose();
            _hdrVersion++;
            Queue();
        }

        internal void InvalidateUi()
        {
            _uiVersion++;
            Queue();
        }

        private void OnLayout(object? sender, EventArgs args)
        {
            ObserveTree();
            Queue();
        }

        private void ObserveTree()
        {
            _tree.Clear();
            _tree.Add(_window);
            foreach (var visual in _window.GetVisualDescendants()) _tree.Add(visual);
            foreach (var visual in _observed)
                if (!_tree.Contains(visual))
                {
                    visual.PropertyChanged -= OnVisualPropertyChanged;
                    if (visual is IPinnedInputCoverageSource source) source.InputCoverageChanged -= InvalidateUi;
                }

            foreach (var visual in _tree)
                if (!_observed.Contains(visual))
                {
                    visual.PropertyChanged += OnVisualPropertyChanged;
                    if (visual is IPinnedInputCoverageSource source) source.InputCoverageChanged += InvalidateUi;
                }

            if (!_observed.SetEquals(_tree)) _uiVersion++;
            _observed.Clear();
            _observed.UnionWith(_tree);
        }

        private void OnVisualPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            InvalidateUi();
        }

        private void Queue()
        {
            if (_disposed || _queued) return;
            _queued = true;
            Dispatcher.UIThread.Post(Update, DispatcherPriority.Loaded);
        }

        private void Update()
        {
            _queued = false;
            if (_disposed) return;
            try
            {
                if (!_window.IsVisible || !_enabled())
                {
                    _backend.Reset();
                    _regionApplied = false;
                    return;
                }

                var scaling = _window.RenderScaling;
                var size = PixelSize.FromSize(_window.ClientSize, scaling);
                if (size.Width <= 0 || size.Height <= 0) return;
                var viewport = _viewportBounds();
                if (_regionApplied && _processedUiVersion == _uiVersion &&
                    _processedHdrVersion == _hdrVersion && _processedViewport == viewport &&
                    _ui?.Size == size && _renderScaling.Equals(scaling)) return;
                if (_hdr is { } frame && viewport is { } bounds &&
                    (frame.Width != bounds.Width || frame.Height != bounds.Height))
                {
                    _refreshHdrGeometry();
                    return;
                }

                if (_ui is null || _ui.Size != size || !_renderScaling.Equals(scaling) ||
                    _renderedUiVersion != _uiVersion)
                {
                    var version = _uiVersion;
                    _ui = _renderer.Render(_window, size, scaling);
                    _renderScaling = scaling;
                    _renderedUiVersion = version;
                }

                var pixels = viewport is not null ? _hdr?.HdrPixels : null;
                var region = PinnedInputRegionBuilder.Build(_ui,
                    pixels is not null ? pixels.AsSpan(0, checked(_hdr!.Width * _hdr.Height * 8)) : [],
                    viewport ?? default, _hdr?.HdrAlphaMode == CaptureAlphaMode.Opaque,
                    _backend.ClipsVisualOutput &&
                    _hdr?.HdrAlphaMode == CaptureAlphaMode.Premultiplied);
                _backend.Apply(region);
                _processedUiVersion = _renderedUiVersion;
                _processedHdrVersion = _hdrVersion;
                _processedViewport = viewport;
                _regionApplied = true;
            }
            catch (Exception error)
            {
                _failed(error);
            }
        }

        private void OnClosed(object? sender, EventArgs args)
        {
            Dispose();
        }
    }
}
