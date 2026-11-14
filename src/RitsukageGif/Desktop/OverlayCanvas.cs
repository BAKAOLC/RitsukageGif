using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas : Control, IDisposable
    {
        private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
        private readonly PixelRect _monitor;
        private readonly int _radius, _threshold;
        private IImage _bitmap;

        private Pen _candidatePen = new(new SolidColorBrush(Color.FromRgb(255, 203, 97)),
            AppearancePreferences.DefaultSelectionBorderWidth, DashStyle.Dash);

        private IBrush _dimBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        private SelectionEdgeDetector _edges;
        private byte[] _pixels;

        private IPen _selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(48, 137, 255)),
            AppearancePreferences.DefaultSelectionBorderWidth);

        private IBrush _selectionBrush = new SolidColorBrush(Color.FromRgb(48, 137, 255));

        public OverlayCanvas(PixelRect monitor, byte[] pixels, int radius = 12, int threshold = 150, bool tiled = false)
        {
            _monitor = monitor;
            _pixels = pixels;
            _edges = new(monitor.Width, monitor.Height, pixels);
            _radius = Math.Clamp(radius, 0, 50);
            _threshold = Math.Clamp(threshold, 1, 765);
            if (tiled)
            {
                _bitmap = new ScreenshotTiledImage(monitor.Width, monitor.Height, pixels);
                return;
            }

            var bitmap = new WriteableBitmap(new(monitor.Width, monitor.Height), new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _bitmap = bitmap;
            using var buffer = bitmap.Lock();
            for (var y = 0; y < monitor.Height; y++)
                Marshal.Copy(pixels, y * monitor.Width * 4,
                    buffer.Address + y * buffer.RowBytes, monitor.Width * 4);
        }

        private bool IsCandidate =>
            !Manipulating && Snapshot?.Phase is null or SelectionPhase.Candidate or SelectionPhase.Pressed;

        public FrozenWindowScreenshot? WindowFrame { get; set; }
        public bool NativeHdrBackground { get; set; }
        internal bool ExternalHintsSurface { get; init; }
        public bool Manipulating { get; set; }
        public PixelRect Selected { get; set; }
        public PixelPoint Mouse { get; set; }
        public int Mode { get; set; }

        public double Scale
        {
            get;
            set
            {
                if (field.Equals(value)) return;
                field = value;
                UpdateSelectionPens();
            }
        } = 1;

        public void Dispose()
        {
            _selectionAnimationTick?.Dispose();
            _selectionAnimationTick = null;
            _magnifierSample?.Dispose();
            _magnifierSamplePixels?.Dispose();
            ((IDisposable)_bitmap).Dispose();
        }

        public void SetVisibleSource(Rect region)
        {
            if (_bitmap is not ScreenshotTiledImage tiled || tiled.VisibleRegion == region) return;
            tiled.VisibleRegion = region;
            InvalidateVisual();
        }

        public PixelPoint Snap(PixelPoint point, int mode)
        {
            if (mode == 0 || _radius == 0) return point;
            var x = point.X - _monitor.X;
            var y = point.Y - _monitor.Y;
            var sx = x;
            var sy = y;
            // Local edge sampling is bounded to the monitor; no giant virtual-screen Sobel bitmap.
            if ((mode & 2) != 0) sx = _edges.Find(x, y, true, _radius, _threshold, Scale);
            if ((mode & 1) != 0) sy = _edges.Find(y, x, false, _radius, _threshold, Scale);
            return new(sx + _monitor.X, sy + _monitor.Y);
        }

        public override void Render(DrawingContext context)
        {
            RenderCore(context, !NativeHdrBackground);
        }

        internal void RenderHdrOverlay(DrawingContext context)
        {
            RenderCore(context, false);
        }

        private void RenderCore(DrawingContext context, bool renderImage)
        {
            var all = new Rect(Bounds.Size);
            if (renderImage)
            {
                context.DrawImage(_bitmap, new(0, 0, _monitor.Width, _monitor.Height), all);
                context.FillRectangle(_dimBrush, all);
            }
            else
            {
                DrawHdrDim(context, all);
            }

            var intersection = Selected.Intersect(_monitor);
            if (!intersection.IsEmpty)
            {
                var local = intersection.ToLocal(_monitor, Scale);
                var destination = new Rect(local.X, local.Y, local.Width, local.Height);
                var source = new Rect(intersection.X - _monitor.X, intersection.Y - _monitor.Y, intersection.Width,
                    intersection.Height);
                if (renderImage)
                {
                    if (SelectionGeometry() is { } shapeGeometry)
                    {
                        using var shapeClip = context.PushGeometryClip(shapeGeometry);
                        context.DrawImage(_bitmap, source, destination);
                        DrawWindowFrame(context, intersection);
                    }
                    else if (_frameDecoration.CornerRadius > 0 && !IsCandidate)
                    {
                        PrepareFrameDecoration();
                        using var rounded = context.PushGeometryClip(_frameClip!);
                        context.DrawImage(_bitmap, source, destination);
                        DrawWindowFrame(context, intersection);
                    }
                    else
                    {
                        context.DrawImage(_bitmap, source, destination);
                        DrawWindowFrame(context, intersection);
                    }
                }

                DrawSelectionBorder(context, intersection, destination);
            }
            else if (IsScreenshotRectangle)
            {
                DrawSelectionBorder(context, intersection, default);
            }

            // The scrollable screenshot preview has its own fixed toolbar; screen HUD coordinates do not apply to the full image.
            DrawFrameDecoration(context);
            if (_bitmap is ScreenshotTiledImage) return;
            DrawSelectionHandles(context);
            if (!ExternalHintsSurface) RenderHints(context);
        }

        internal void RenderHints(DrawingContext context)
        {
            if (_bitmap is ScreenshotTiledImage) return;
            var mouse = new Point((Mouse.X - _monitor.X) / Scale, (Mouse.Y - _monitor.Y) / Scale);
            var ownsHints = _monitor.Contains(Mouse);
            var pointer = MagnifierPointer ?? Mouse;
            var sampleCanvas = MagnifierSampleCanvas ?? (ownsHints ? this : null);
            _hintStatusBounds = _hintActionsBounds = default;
            _hintMagnifierBounds = sampleCanvas is not null && _monitor.Contains(pointer)
                ? GetMagnifierFrame(new((pointer.X - _monitor.X) / Scale, (pointer.Y - _monitor.Y) / Scale))
                : default;

            if (ownsHints) DrawHint(context, mouse, true);
            DrawAuxiliaryLines(context, mouse);
            if (ownsHints) DrawHint(context, mouse);
            if (sampleCanvas is null || _hintMagnifierBounds is not { Width: > 0, Height: > 0 } lens) return;
            context.DrawRectangle(HintBackground, HintBorder, lens, 6, 6);
            sampleCanvas.DrawMagnifier(context, new(lens.X, lens.Y, MagnifierWidth, MagnifierHeight));
            sampleCanvas.DrawMagnifierDetails(context, lens);
        }
    }
}
