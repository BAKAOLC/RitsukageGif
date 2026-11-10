using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageTextSelectionSurface : Control
    {
        private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);
        private IPointer? _pointer;
        private ImageTextSelection _selection = new(ImageTextSelectionDocument.Empty);
        private IReadOnlyList<Rect> _highlights = [];

        internal ImageTextSelectionSurface()
        {
            Focusable = true;
            ClipToBounds = true;
            PointerPressed += Pressed;
            PointerMoved += Moved;
            PointerReleased += Released;
            PointerCaptureLost += (_, _) =>
            {
                _pointer = null;
                _selection.Finish();
                InvalidateVisual();
            };
            KeyDown += (_, args) =>
            {
                var gestures = Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy;
                if (!_selection.HasSelection || gestures?.Any(gesture => gesture.Matches(args)) != true) return;
                CopyRequested?.Invoke(_selection.SelectedText);
                args.Handled = true;
            };
        }

        internal IBrush SelectionBrush { get; init; } = Brushes.DodgerBlue;

        internal IImage? Source
        {
            get;
            set
            {
                field = value;
                InvalidateVisual();
            }
        }

        internal ImageTextSelectionDocument Document
        {
            set
            {
                if (ReferenceEquals(field, value)) return;
                field = value;
                _pointer?.Capture(null);
                _pointer = null;
                _selection = new(value);
                Cursor = null;
                InvalidateVisual();
            }
        } = ImageTextSelectionDocument.Empty;

        internal event Action<string>? CopyRequested;

        internal void SetHighlights(IReadOnlyList<Rect> highlights)
        {
            _highlights = highlights;
            InvalidateVisual();
        }

        private void Pressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            if (args.ClickCount == 2 && _selection.SelectLine(args.GetPosition(this)))
            {
                Focus();
                args.Handled = true;
                InvalidateVisual();
                return;
            }

            if (!_selection.Begin(args.GetPosition(this)))
            {
                InvalidateVisual();
                return;
            }

            Focus();
            _pointer = args.Pointer;
            args.Pointer.Capture(this);
            args.Handled = true;
        }

        private void Moved(object? sender, PointerEventArgs args)
        {
            if (_pointer == args.Pointer && _selection.Extend(args.GetPosition(this)))
            {
                args.Handled = true;
                InvalidateVisual();
            }
            else
            {
                Cursor = _selection.Hit(args.GetPosition(this)) >= 0 ? TextCursor : null;
            }
        }

        private void Released(object? sender, PointerReleasedEventArgs args)
        {
            if (_pointer != args.Pointer || args.InitialPressMouseButton != MouseButton.Left) return;
            _selection.Finish();
            _pointer = null;
            args.Pointer.Capture(null);
            args.Handled = true;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (Source is { } image) context.DrawImage(image, new(Bounds.Size));
            using var opacity = context.PushOpacity(.35);
            foreach (var span in _selection.SelectedSpans)
                context.DrawRectangle(SelectionBrush, null, span.Bounds);
            foreach (var rectangle in _highlights)
                context.DrawRectangle(SelectionBrush, null, rectangle);
        }
    }
}
