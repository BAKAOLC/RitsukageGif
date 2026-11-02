using Avalonia;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageEditorStripSelection
    {
        private double _anchor;
        private double _beforeEnd;
        private double _beforeStart;
        private int _dragEdge;
        internal ImageStripAxis? Axis { get; private set; }
        private double Start { get; set; }
        private double End { get; set; }
        internal bool IsDragging { get; private set; }
        internal bool HasSelection => End > Start;
        internal int FirstPixel => (int)Math.Floor(Start);
        internal int PixelCount => (int)Math.Ceiling(End) - FirstPixel;

        internal void Activate(ImageStripAxis? axis)
        {
            Axis = axis;
            Start = End = 0;
            IsDragging = false;
        }

        internal Rect Bounds(PixelRect image)
        {
            return Axis == ImageStripAxis.Rows
                ? new(0, Start, image.Width, Math.Max(0, End - Start))
                : new(Start, 0, Math.Max(0, End - Start), image.Height);
        }

        internal StandardCursorType CursorAt(Point point, double controlScale)
        {
            if (!HasSelection) return StandardCursorType.Arrow;
            var value = Coordinate(point);
            if (Math.Min(Math.Abs(value - Start), Math.Abs(value - End)) > 6 * controlScale)
                return StandardCursorType.Arrow;
            return Axis == ImageStripAxis.Rows ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast;
        }

        internal void Begin(Point point, PixelRect image, double controlScale)
        {
            _anchor = Math.Clamp(Coordinate(point), 0, Extent(image));
            _beforeStart = Start;
            _beforeEnd = End;
            if (!HasSelection)
            {
                _dragEdge = 3;
                Start = End = _anchor;
            }
            else if (Math.Min(Math.Abs(_anchor - Start), Math.Abs(_anchor - End)) <= 6 * controlScale ||
                     _anchor < Start || _anchor > End)
            {
                _dragEdge = Math.Abs(_anchor - Start) <= Math.Abs(_anchor - End) ? 1 : 2;
            }
            else
            {
                _dragEdge = 0;
            }

            IsDragging = true;
        }

        internal void Move(Point point, PixelRect image)
        {
            if (!IsDragging) return;
            var extent = Extent(image);
            var value = Math.Clamp(Coordinate(point), 0, extent);
            switch (_dragEdge)
            {
                case 0:
                    var shift = Math.Clamp(value - _anchor, -_beforeStart, extent - _beforeEnd);
                    Start = _beforeStart + shift;
                    End = _beforeEnd + shift;
                    break;
                case 1:
                    Start = Math.Min(value, _beforeEnd);
                    End = Math.Max(value, _beforeEnd);
                    break;
                case 2:
                    Start = Math.Min(_beforeStart, value);
                    End = Math.Max(_beforeStart, value);
                    break;
                default:
                    Start = Math.Min(_anchor, value);
                    End = Math.Max(_anchor, value);
                    break;
            }
        }

        internal void EndDrag(Point point, PixelRect image)
        {
            Move(point, image);
            FinishDrag();
        }

        internal void FinishDrag()
        {
            IsDragging = false;
            if (End - Start < 1) Start = End = 0;
        }

        private double Coordinate(Point point)
        {
            return Axis == ImageStripAxis.Rows ? point.Y : point.X;
        }

        private double Extent(PixelRect image)
        {
            return Axis == ImageStripAxis.Rows ? image.Height : image.Width;
        }
    }
}
