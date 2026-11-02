using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageViewport : UserControl
    {
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly Border _imageBounds = new();
        private readonly ScrollViewer _scroll;

        private readonly LayoutTransformControl _transform = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private bool _fitUpscale;
        private bool _fitting = true;
        private Vector _panOffset;
        private Point _panStart;
        private Vector? _pendingOffset;
        private PixelSize _pixelSize;
        private IPointer? _pointer;
        private long _revision;
        private TopLevel? _root;

        internal ImageViewport()
        {
            _transform.Child = _imageBounds;
            _scroll = new()
            {
                Content = _transform,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Content = _scroll;
            _scroll.ScrollChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
            _scroll.SizeChanged += (_, _) =>
            {
                if (_fitting) ApplyFit();
            };
            AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Bubble);
            AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
            PointerCaptureLost += (_, _) => _pointer = null;
        }

        public Control? ImageContent
        {
            set
            {
                _imageBounds.Child = value;
                UpdateSize();
            }
        }

        public IBrush? ImageBacking
        {
            set => _imageBounds.Background = value;
        }

        public PixelSize PixelSize
        {
            set
            {
                _pixelSize = value;
                UpdateSize();
                if (_fitting) ApplyFit();
            }
        }

        public IImage? Source
        {
            set
            {
                _image.Source = value;
                ImageContent = _image;
                PixelSize = value switch
                {
                    Bitmap bitmap => bitmap.PixelSize,
                    null => default,
                    _ => new((int)Math.Ceiling(value.Size.Width), (int)Math.Ceiling(value.Size.Height)),
                };
            }
        }

        public bool PanEnabled
        {
            get;
            set
            {
                field = value;
                if (value) return;
                _pointer?.Capture(null);
                _pointer = null;
            }
        } = true;

        public double Zoom { get; private set; } = 1;

        public Rect ExactVisibleContentRect
        {
            get
            {
                var margin = CenterMargin(Factor);
                var left = Math.Clamp((_scroll.Offset.X - margin.X) / Factor, 0, _pixelSize.Width);
                var top = Math.Clamp((_scroll.Offset.Y - margin.Y) / Factor, 0, _pixelSize.Height);
                var right = Math.Clamp((_scroll.Offset.X + _scroll.Viewport.Width - margin.X) / Factor, left,
                    _pixelSize.Width);
                var bottom = Math.Clamp((_scroll.Offset.Y + _scroll.Viewport.Height - margin.Y) / Factor, top,
                    _pixelSize.Height);
                return new(left, top, right - left, bottom - top);
            }
        }

        private double Scaling => _root?.RenderScaling ?? 1;
        private double Factor { get; set; } = 1;

        public event EventHandler? ViewChanged;

        public void ActualSize()
        {
            SetZoom(1);
        }

        public void FitToViewport()
        {
            _fitting = true;
            _fitUpscale = true;
            ApplyFit();
        }

        private void ApplyFit()
        {
            if (_pixelSize.Width == 0 || _pixelSize.Height == 0 || _scroll.Viewport.Width <= 0 ||
                _scroll.Viewport.Height <= 0) return;
            var zoom = Math.Min(_scroll.Viewport.Width / _pixelSize.Width,
                _scroll.Viewport.Height / _pixelSize.Height) * Scaling;
            ApplyZoom(_fitUpscale ? zoom : Math.Min(1, zoom), null);
        }

        public void SetZoom(double zoom, Point? viewportAnchor = null)
        {
            if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
            _fitting = false;
            ApplyZoom(Math.Clamp(zoom, 0.000001, 128), viewportAnchor);
        }

        private void ApplyZoom(double zoom, Point? anchor)
        {
            var point = anchor ?? new Point(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
            var margin = CenterMargin(Factor);
            var offset = _pendingOffset ?? _scroll.Offset;
            var pixel = new Point((point.X + offset.X - margin.X) / Factor,
                (point.Y + offset.Y - margin.Y) / Factor);
            Zoom = zoom;
            Factor = zoom / Scaling;
            var centered = CenterMargin(Factor);
            _pendingOffset = new(
                Math.Clamp(pixel.X * Factor + centered.X - point.X, 0,
                    Math.Max(0, _pixelSize.Width * Factor - _scroll.Viewport.Width)),
                Math.Clamp(pixel.Y * Factor + centered.Y - point.Y, 0,
                    Math.Max(0, _pixelSize.Height * Factor - _scroll.Viewport.Height)));
            _transform.LayoutTransform = new ScaleTransform(Factor, Factor);
            var revision = ++_revision;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision != _revision) return;
                _transform.UpdateLayout();
                _scroll.Offset = _pendingOffset ?? _scroll.Offset;
                _pendingOffset = null;
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Loaded);
        }

        private Point CenterMargin(double factor)
        {
            return new(Math.Max(0, (_scroll.Viewport.Width - _pixelSize.Width * factor) / 2),
                Math.Max(0, (_scroll.Viewport.Height - _pixelSize.Height * factor) / 2));
        }

        private void UpdateSize()
        {
            _imageBounds.Width = _pixelSize.Width;
            _imageBounds.Height = _pixelSize.Height;
            if (_imageBounds.Child is not { } child) return;
            child.Width = _pixelSize.Width;
            child.Height = _pixelSize.Height;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _root = TopLevel.GetTopLevel(this);
            if (_root is not null) _root.ScalingChanged += ScalingChanged;
            if (_fitting) ApplyFit();
            else ApplyZoom(Zoom, null);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_root is not null) _root.ScalingChanged -= ScalingChanged;
            _root = null;
            _revision++;
            _pointer?.Capture(null);
            _pointer = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void ScalingChanged(object? sender, EventArgs e)
        {
            if (_fitting) ApplyFit();
            else ApplyZoom(Zoom, null);
        }

        private void Wheel(object? sender, PointerWheelEventArgs e)
        {
            if (!PanEnabled || e.Delta.Y == 0 || IsScrollBar(e.Source)) return;
            SetZoom(Zoom * Math.Pow(1.1, e.Delta.Y), e.GetPosition(_scroll));
            e.Handled = true;
        }

        private void Pressed(object? sender, PointerPressedEventArgs e)
        {
            if (!PanEnabled || IsScrollBar(e.Source)) return;
            var point = e.GetCurrentPoint(_scroll);
            if (point.Properties.IsMiddleButtonPressed)
            {
                if (Math.Abs(Zoom - 1) < .000001) FitToViewport();
                else ActualSize();
                e.Handled = true;
            }
            else if (point.Properties.IsLeftButtonPressed)
            {
                _panStart = point.Position;
                _panOffset = _scroll.Offset;
                _pointer = e.Pointer;
                _pointer.Capture(this);
                e.Handled = true;
            }
        }

        private void Moved(object? sender, PointerEventArgs e)
        {
            if (_pointer != e.Pointer) return;
            var delta = e.GetPosition(_scroll) - _panStart;
            _scroll.Offset = new(Math.Max(0, _panOffset.X - delta.X), Math.Max(0, _panOffset.Y - delta.Y));
            e.Handled = true;
        }

        private static bool IsScrollBar(object? source)
        {
            return source is Visual visual &&
                   (visual is ScrollBar || visual.GetVisualAncestors().Any(parent => parent is ScrollBar));
        }

        private void Released(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer != e.Pointer) return;
            _pointer.Capture(null);
            _pointer = null;
            e.Handled = true;
        }
    }
}
