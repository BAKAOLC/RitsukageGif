using System.Globalization;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichView
    {
        private bool _selecting;

        private void ConfigureInput()
        {
            _surface.PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed ||
                    args.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
                if (_layout is null) return;
                var point = args.GetPosition(_surface);
                if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && _layout.LinkAt(point) is { } link)
                {
                    LinkRequested?.Invoke(link);
                    args.Handled = true;
                    return;
                }

                if (!_selectable) return;
                _surface.Focus();
                _caret = _layout.HitTest(point);
                if (!args.KeyModifiers.HasFlag(KeyModifiers.Shift)) _anchor = _caret;
                _selecting = true;
                args.Pointer.Capture(_surface);
                _surface.InvalidateVisual();
                args.Handled = true;
            };
            _surface.PointerMoved += (_, args) =>
            {
                if (!_selecting || _layout is null) return;
                var point = args.GetPosition(_surface);
                _caret = _layout.HitTest(point);
                RevealCaret();
                _surface.InvalidateVisual();
                args.Handled = true;
            };
            _surface.PointerReleased += (_, args) =>
            {
                if (!_selecting) return;
                _selecting = false;
                args.Pointer.Capture(null);
                args.Handled = true;
            };
            _surface.PointerCaptureLost += (_, _) => _selecting = false;
            _surface.KeyDown += (_, args) =>
            {
                if (!_selectable || _layout is null || args.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
                var text = Document.PlainText;
                var rect = _layout.CaretBounds(_caret);
                int target;
                var control = args.KeyModifiers.HasFlag(KeyModifiers.Control);
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (args.Key)
                {
                    case Key.Left: target = PreviousElement(text, _caret); break;
                    case Key.Right:
                        target = _caret < text.Length
                            ? _caret + StringInfo.GetNextTextElementLength(text.AsSpan(_caret))
                            : text.Length; break;
                    case Key.Up: target = _layout.HitTest(new(rect.X, rect.Y - rect.Height / 2)); break;
                    case Key.Down: target = _layout.HitTest(new(rect.X, rect.Bottom + rect.Height / 2)); break;
                    case Key.Home: target = control ? 0 : _layout.HitTest(new(0, rect.Center.Y)); break;
                    case Key.End:
                        target = control ? text.Length : _layout.HitTest(new(_layout.Size.Width, rect.Center.Y)); break;
                    case Key.PageUp:
                        target = _layout.HitTest(new(rect.X, Math.Max(0, rect.Y - Viewport.Height))); break;
                    case Key.PageDown: target = _layout.HitTest(new(rect.X, rect.Y + Viewport.Height)); break;
                    default: return;
                }

                _caret = target;
                if (!args.KeyModifiers.HasFlag(KeyModifiers.Shift)) _anchor = _caret;
                RevealCaret();
                _surface.InvalidateVisual();
                args.Handled = true;
            };
        }

        private void RevealCaret()
        {
            if (_layout is null) return;
            var caret = _layout.CaretBounds(_caret);
            if (caret.Top < Offset.Y) Offset = new(Offset.X, caret.Top);
            else if (caret.Bottom > Offset.Y + Viewport.Height)
                Offset = new(Offset.X, Math.Max(0, caret.Bottom - Viewport.Height));
        }

        private static int PreviousElement(string text, int position)
        {
            if (position == 0) return 0;
            var offset = 0;
            var previous = 0;
            while (offset < position)
            {
                previous = offset;
                offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            }

            return previous;
        }
    }
}
