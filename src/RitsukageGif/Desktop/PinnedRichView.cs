using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichView : ScrollViewer, IPinnedDocumentTextView, IDisposable
    {
        private readonly Dictionary<string, PinnedRichResource> _leases = [];
        private readonly CancellationTokenSource _lifetime = new();
        private readonly HashSet<string> _loading = [];
        private readonly Dictionary<string, Size> _naturalSizes = [];
        private readonly bool _ownsResources;
        private readonly Dictionary<string, string> _resourceErrors = [];
        private readonly PinnedRichResourceLoader _resources;
        private readonly PinnedRichSurface _surface;
        private int _anchor;
        private int _caret;
        private bool _disposed;
        private double _formatWidth = 440;
        private PinnedRichLayout? _layout;
        private bool _lifetimeDisposed;
        private double _restoreDistance;
        private int? _restorePosition;
        private bool _selectable;
        private HashSet<string> _visibleImages = [];

        internal PinnedRichView(PinnedRichDocument document, PinnedRichResourceLoader resources, IBrush foreground,
            bool ownsResources = true)
        {
            Document = document;
            _resources = resources;
            _ownsResources = ownsResources;
            Foreground = foreground;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _surface = new(this) { Focusable = true };
            Content = _surface;
            ScrollChanged += (_, args) =>
            {
                if (Math.Abs(args.ViewportDelta.X) > .1 && Viewport.Width > 0)
                {
                    _formatWidth = Viewport.Width;
                    _surface.InvalidateMeasure();
                }

                UpdateVisibleResources();
                _surface.InvalidateVisual();
                ScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
            };
            ConfigureInput();
        }

        protected override Type StyleKeyOverride => typeof(ScrollViewer);

        public Control Control => this;
        public string SelectedText => Document.PlainText[SelectionStart..SelectionEnd];

        public string SelectedHtml =>
            PinnedRichHtmlWriter.Write(Document, SelectionStart, SelectionEnd - SelectionStart);

        public Vector ScrollOffset
        {
            get => Offset;
            set => Offset = value;
        }

        private int SelectionStart => Math.Min(_anchor, _caret);
        private int SelectionEnd => Math.Max(_anchor, _caret);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            foreach (var lease in _leases.Values) lease.Dispose();
            _leases.Clear();
            _layout?.Dispose();
            _layout = null;
            if (_ownsResources) _resources.Dispose();
            DisposeLifetimeIfIdle();
        }
        private PinnedRichDocument Document { get; }

        private Rect VisibleBounds =>
            new(Offset.X, Offset.Y, Math.Max(1, Viewport.Width), Math.Max(1, Viewport.Height));

        internal double PreferredWidth => _layout!.PreferredWidth + Padding.Left + Padding.Right +
                                          BorderThickness.Left + BorderThickness.Right;

        internal double FormattedWidth { get; private set; }

        public event EventHandler? ScrollOffsetChanged;

        public void SetSelectable(bool value)
        {
            _selectable = value;
            if (!value) ClearSelection();
        }

        public void SelectAll()
        {
            Select(0, Document.PlainText.Length);
        }

        public void ReleaseContent()
        {
            Dispose();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (double.IsFinite(availableSize.Width))
                _formatWidth = Math.Max(32,
                    availableSize.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right);
            return base.MeasureOverride(availableSize);
        }

        private void ClearSelection()
        {
            Select(_caret, 0);
        }

        internal event Action<string>? LinkRequested;

        internal void PrepareForFit()
        {
            ApplyStyling();
            ApplyTemplate();
            Presenter!.SetCurrentValue(ContentPresenter.ContentProperty, Content);
            Presenter.UpdateChild();
        }

        private void Select(int start, int length)
        {
            _anchor = Math.Clamp(start, 0, Document.PlainText.Length);
            _caret = Math.Clamp(start + length, 0, Document.PlainText.Length);
            _surface.InvalidateVisual();
        }

        private void EnsureLayout(double width)
        {
            width = Math.Max(32, width);
            if (_layout is not null && Math.Abs(width - FormattedWidth) < .1) return;
            CaptureLayoutAnchor();
            _layout?.Dispose();
            FormattedWidth = width;
            _layout = new(Document, width, Foreground ?? Brushes.Black, NaturalSize, DrawImage, _lifetime.Token);
            SceneContentChanged?.Invoke(this, EventArgs.Empty);
            if (_restorePosition is not { } position) return;
            var top = Math.Max(0, _layout.CaretBounds(position).Top + _restoreDistance);
            _restorePosition = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed) Offset = new(0, top);
            }, DispatcherPriority.Loaded);
        }

        private void CaptureLayoutAnchor()
        {
            if (_layout is null || _restorePosition is not null) return;
            _restorePosition = _layout.HitTest(new(Offset.X, Offset.Y));
            _restoreDistance = Offset.Y - _layout.CaretBounds(_restorePosition.Value).Top;
        }

        private Size NaturalSize(PinnedRichImage image)
        {
            return _naturalSizes.GetValueOrDefault(image.Source, new(32, 32));
        }

        private void DrawImage(DrawingContext context, PinnedRichImage image, Rect bounds)
        {
            if (_leases.TryGetValue(image.Source, out var resource) && resource.Bitmap is { } bitmap)
            {
                context.DrawImage(bitmap, new(bitmap.Size), bounds);
            }
            else
            {
                context.DrawRectangle(null, new Pen(Brushes.Gray), bounds);
                var alternative = image.Alternative.Length > 0
                    ? image.Alternative
                    : _resourceErrors.GetValueOrDefault(image.Source, "");
                if (alternative.Length > 0)
                {
                    using var label = new TextLayout(alternative,
                        Typeface.Default, 12, Foreground, maxWidth: bounds.Width, maxHeight: bounds.Height);
                    using var clip = context.PushClip(bounds);
                    label.Draw(context, bounds.Position);
                }
            }
        }

        private void UpdateVisibleResources()
        {
            if (_layout is null || _disposed) return;
            var overscan = VisibleBounds.Inflate(new Thickness(0, Viewport.Height));
            _visibleImages = _layout.ImageSources(overscan).ToHashSet(StringComparer.Ordinal);
            foreach (var source in _leases.Keys.Where(source => !_visibleImages.Contains(source)).ToArray())
            {
                _leases.Remove(source, out var lease);
                lease?.Dispose();
            }

            foreach (var source in _visibleImages)
                if (!_leases.ContainsKey(source) && _loading.Add(source))
                    _ = LoadVisibleAsync(source);
        }

        private async Task LoadVisibleAsync(string source)
        {
            PinnedRichResource? lease = null;
            try
            {
                lease = await _resources.AcquireAsync(source, Document.BaseUri, _lifetime.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    if (lease.Error is { } error) _resourceErrors[source] = error;
                    else _resourceErrors.Remove(source);
                    if (lease.Bitmap is { } bitmap)
                    {
                        var previous = _naturalSizes.GetValueOrDefault(source, new(32, 32));
                        _naturalSizes[source] = bitmap.Size;
                        if (previous != bitmap.Size)
                        {
                            CaptureLayoutAnchor();
                            _layout?.Dispose();
                            _layout = null;
                            _surface.InvalidateMeasure();
                        }
                    }

                    if (_visibleImages.Contains(source))
                    {
                        _leases.Add(source, lease);
                        lease = null;
                    }

                    _surface.InvalidateVisual();
                    SceneContentChanged?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            finally
            {
                lease?.Dispose();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _loading.Remove(source);
                    DisposeLifetimeIfIdle();
                });
            }
        }

        private void DisposeLifetimeIfIdle()
        {
            if (!_disposed || _loading.Count != 0 || _lifetimeDisposed) return;
            _lifetimeDisposed = true;
            _lifetime.Dispose();
        }
    }
}
