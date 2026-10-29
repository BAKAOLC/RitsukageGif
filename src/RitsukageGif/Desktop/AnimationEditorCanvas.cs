using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using FontWeight = Avalonia.Media.FontWeight;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas : Control, IDisposable
    {
        private static readonly IBrush CanvasBackground = new SolidColorBrush(Color.FromRgb(30, 32, 37));
        private readonly List<AnimationEditorLayer> _layers = [];
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private WriteableBitmap? _bitmap;
        private bool _changed;
        private bool _disposed;
        private bool _fit = true;
        private int _height;
        private int _loadVersion;
        private Vector _pan;
        private string? _path;
        private byte[]? _pixels;
        private AnimationEditorLayer? _selected;

        private int _width;
        private double _zoom = 1;

        public AnimationEditorCanvas()
        {
            Focusable = true;
        }

        public AnimationEditorTool Tool
        {
            get;
            set
            {
                if (field == value) return;
                CancelGesture();
                field = value;
                InvalidateVisual();
            }
        }

        public Color BrushColor { get; set; } = Colors.Red;
        public double BrushSize { get; set; } = 4;
        public bool Filled { get; set; }
        public Rect? Selection { get; private set; }
        public bool HasChanges => _changed || _layers.Count != 0;
        public string? SelectedText => _selected?.Text;
        public string? SelectedTextFamily => _selected?.FontFamily;
        public bool SelectedTextBold => _selected?.Bold ?? false;
        public bool SelectedTextItalic => _selected?.Italic ?? false;
        public Color SelectedTextColor => _selected?.Color ?? BrushColor;
        public double SelectedTextSize => _selected?.FontSize ?? 24;
        public Size? SelectedImageSize => _selected?.Image is null ? null : _selected.Bounds.Size;
        public double SelectedOpacity => _selected?.Opacity ?? 1;
        public Rect? SelectedBounds => _selected?.Bounds;
        public bool HasSelectedLayer => _selected is not null;

        public double Zoom
        {
            get => EffectiveZoom;
            set
            {
                if (!double.IsFinite(value)) return;
                CancelGesture();
                _zoom = Math.Clamp(value, .02, 32);
                _fit = false;
                InvalidateVisual();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private double EffectiveZoom => _fit && _width > 0 && _height > 0
            ? Math.Max(.001,
                Math.Min(Math.Max(1, Bounds.Width - 24) / _width, Math.Max(1, Bounds.Height - 24) / _height))
            : _zoom;

        private Point Origin => new((Bounds.Width - _width * EffectiveZoom) / 2 + _pan.X,
            (Bounds.Height - _height * EffectiveZoom) / 2 + _pan.Y);

        public void Dispose()
        {
            _disposed = true;
            Interlocked.Increment(ref _loadVersion);
            CancelGesture();
            ClearLayers();
            ClearHistory();
            _bitmap?.Dispose();
            _bitmap = null;
            _pixels = null;
        }

        public event EventHandler? DraftChanged;
        public event EventHandler? SelectionChanged;
        public event EventHandler? ViewChanged;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != BoundsProperty || change.OldValue is not Rect previous ||
                change.NewValue is not Rect current || previous.Size == current.Size) return;
            if (_fit && _width > 0 && _height > 0) ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private Point ToImage(Point point)
        {
            return new((point.X - Origin.X) / EffectiveZoom, (point.Y - Origin.Y) / EffectiveZoom);
        }

        public async Task LoadFrameAsync(string path, CancellationToken token = default)
        {
            var version = Interlocked.Increment(ref _loadVersion);
            await _loadGate.WaitAsync(token);
            try
            {
                if (_disposed || version != _loadVersion) return;
                var frame = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return DecodeFrame(path);
                }, token);
                token.ThrowIfCancellationRequested();
                if (_disposed || version != _loadVersion) return;
                SetFrame(path, frame);
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private static AnimationEditorFrameData DecodeFrame(string path)
        {
            using var image = new MagickImage();
            image.Ping(path);
            if ((long)image.Width * image.Height > 64 * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("AnimationEditorImageTooLarge"));
            image.Read(path);
            using var pixels = image.GetPixels();
            return new(checked((int)image.Width), checked((int)image.Height),
                pixels.ToByteArray(PixelMapping.BGRA)!);
        }

        private void SetFrame(string path, AnimationEditorFrameData frame)
        {
            if (_disposed) return;
            WriteableBitmap? replacement = null;
            if (_bitmap?.PixelSize != new PixelSize(frame.Width, frame.Height))
                replacement = new(new(frame.Width, frame.Height), new(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            CancelGesture();
            ClearLayers();
            ClearHistory();
            _width = frame.Width;
            _height = frame.Height;
            _pixels = frame.Pixels;
            if (replacement is not null)
            {
                _bitmap?.Dispose();
                _bitmap = replacement;
            }

            _path = path;
            _changed = false;
            Selection = null;
            Upload();
            DraftChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Fit()
        {
            CancelGesture();
            _fit = true;
            _pan = default;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task ResetDraftAsync(CancellationToken token = default)
        {
            return _path is { } path ? LoadFrameAsync(path, token) : Task.CompletedTask;
        }

        private void Upload()
        {
            if (_bitmap is null || _pixels is null) return;
            using var buffer = _bitmap.Lock();
            if (buffer.RowBytes == _width * 4) Marshal.Copy(_pixels, 0, buffer.Address, _pixels.Length);
            else
                for (var y = 0; y < _height; y++)
                    Marshal.Copy(_pixels, y * _width * 4, buffer.Address + y * buffer.RowBytes, _width * 4);
            InvalidateVisual();
        }

        private void Changed()
        {
            _changed = true;
            InvalidateVisual();
            DraftChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(CanvasBackground, new(Bounds.Size));
            if (_bitmap is null) return;
            using var clip = context.PushClip(new(Bounds.Size));
            var origin = Origin;
            var zoom = EffectiveZoom;
            using var transform = context.PushTransform(new(zoom, 0, 0, zoom, origin.X, origin.Y));
            var frame = new Rect(0, 0, _width, _height);
            using (context.PushClip(frame))
            {
                // Tile in screen space so very large images never create millions of checker cells.
                var tile = 12 / zoom;
                var visible =
                    frame.Intersect(new(ToImage(default), ToImage(new(Bounds.Width, Bounds.Height))));
                for (var y = Math.Floor(visible.Y / tile); y < Math.Ceiling(visible.Bottom / tile); y++)
                for (var x = Math.Floor(visible.X / tile); x < Math.Ceiling(visible.Right / tile); x++)
                    context.FillRectangle(((long)x + (long)y) % 2 == 0 ? Brushes.LightGray : Brushes.White,
                        new(x * tile, y * tile, tile, tile));
                DrawContent(context);
                if (Selection is { } selection)
                    context.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 1 / zoom, DashStyle.Dash), selection);
                if (_dragging && Tool is AnimationEditorTool.Line or AnimationEditorTool.Arrow
                        or AnimationEditorTool.Rectangle
                        or AnimationEditorTool.Ellipse)
                    DrawShape(context, _start, _last);
            }

            DrawLayerSelection(context, zoom);
        }

        private void DrawContent(DrawingContext context)
        {
            if (_bitmap is null) return;
            context.DrawImage(_bitmap, new(0, 0, _width, _height));
            foreach (var layer in _layers)
            {
                using var opacity = context.PushOpacity(layer.Opacity);
                if (layer.Image is { } image) context.DrawImage(image, layer.Bounds);
                else if (layer.Formatted is { } formatted) context.DrawText(formatted, layer.Bounds.TopLeft);
            }
        }

        public byte[] ExportPng()
        {
            if (_bitmap is null) throw new InvalidOperationException();
            using var target = new RenderTargetBitmap(new(_width, _height), new(96, 96));
            using (var context = target.CreateDrawingContext()) DrawContent(context);
            using var stream = new MemoryStream();
            target.Save(stream);
            return stream.ToArray();
        }

        public void AddText(string text, double fontSize, Color color, string? fontFamily = null, bool bold = false,
            bool italic = false, double opacity = 1)
        {
            if (_bitmap is null) return;
            Remember();
            var layer = new AnimationEditorLayer { Text = text, Bounds = new(_width * .1, _height * .1, 1, 1) };
            _layers.Add(layer);
            _selected = layer;
            SetSelectedText(text, fontSize, color, fontFamily, bold, italic, opacity);
        }

        public void UpdateSelectedText(string text, double fontSize, Color color, string? fontFamily = null,
            bool bold = false, bool italic = false, double opacity = 1)
        {
            if (_selected is not { Image: null }) return;
            Remember();
            SetSelectedText(text, fontSize, color, fontFamily, bold, italic, opacity);
        }

        private void SetSelectedText(string text, double fontSize, Color color, string? fontFamily, bool bold,
            bool italic, double opacity)
        {
            if (_selected is not { Image: null } layer || !double.IsFinite(fontSize)) return;
            layer.Text = text;
            layer.FontSize = Math.Clamp(fontSize, 1, 4096);
            layer.FontFamily = fontFamily;
            layer.Bold = bold;
            layer.Italic = italic;
            layer.Color = color;
            layer.Opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
            layer.Formatted = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new(string.IsNullOrWhiteSpace(fontFamily) ? FontFamily.Default : new(fontFamily),
                    italic ? FontStyle.Italic : FontStyle.Normal,
                    bold ? FontWeight.Bold : FontWeight.Normal), layer.FontSize,
                new SolidColorBrush(color));
            layer.Bounds = new(layer.Bounds.Position,
                new Size(Math.Max(1, layer.Formatted.WidthIncludingTrailingWhitespace),
                    Math.Max(1, layer.Formatted.Height)));
            Changed();
        }

        public async Task AddImageAsync(string path, double opacity = 1, CancellationToken token = default)
        {
            if (_bitmap is null || _disposed) return;
            var version = _loadVersion;
            await _loadGate.WaitAsync(token);
            Bitmap? bitmap = null;
            try
            {
                if (_disposed || version != _loadVersion) return;
                bitmap = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var metadata = new MagickImage();
                    metadata.Ping(path);
                    if ((long)metadata.Width * metadata.Height > 64 * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("AnimationEditorImageTooLarge"));
                    return new Bitmap(path);
                }, token);
                token.ThrowIfCancellationRequested();
                if (_disposed || version != _loadVersion) return;
                AddImage(bitmap, opacity);
                bitmap = null;
            }
            finally
            {
                bitmap?.Dispose();
                _loadGate.Release();
            }
        }

        private void AddImage(Bitmap bitmap, double opacity)
        {
            Remember();
            _ownedImages.Add(bitmap);
            var ratio = Math.Min(1,
                Math.Min(_width * .5 / bitmap.PixelSize.Width, _height * .5 / bitmap.PixelSize.Height));
            var layer = new AnimationEditorLayer
            {
                Image = bitmap,
                Bounds = new(_width * .1, _height * .1, bitmap.PixelSize.Width * ratio,
                    bitmap.PixelSize.Height * ratio),
                Opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1,
            };
            _layers.Add(layer);
            _selected = layer;
            Changed();
        }

        public void UpdateSelectedImage(double width, double height, double opacity)
        {
            if (_selected is not { Image: not null } layer || !double.IsFinite(width) || !double.IsFinite(height) ||
                !double.IsFinite(opacity)) return;
            Remember();
            layer.Bounds = new(layer.Bounds.Position,
                new Size(Math.Clamp(width, 1, 65536), Math.Clamp(height, 1, 65536)));
            layer.Opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
            Changed();
        }

        public void DeleteSelected()
        {
            if (_selected is not { } layer) return;
            Remember();
            _layers.Remove(layer);
            _selected = null;
            Changed();
        }

        private void ClearLayers()
        {
            _layers.Clear();
            _selected = null;
        }
    }
}
