using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class GlobalMouseGestureTracker(
        IReadOnlyDictionary<InputMouseGesture, string> bindings,
        int dragWidth,
        int dragHeight)
    {
        private int _button;
        private bool _dragging;
        private KeyModifiers _modifiers;
        private int _pressedButtons;
        private int _startX, _startY;

        internal string? Process(uint message, GlobalMouseHookData data, KeyModifiers modifiers,
            out PixelRect? region)
        {
            region = null;
            if ((data.Flags & 3) != 0) return null;
            var button = message switch
            {
                0x0201 or 0x0202 => 1,
                0x0204 or 0x0205 => 2,
                0x0207 or 0x0208 => 3,
                0x020B or 0x020C => (data.MouseData >> 16) switch { 1 => 4, 2 => 5, _ => 0 },
                _ => 0,
            };
            if (message is 0x0201 or 0x0204 or 0x0207 or 0x020B)
            {
                if (button == 0) return null;
                var wasPressed = _pressedButtons != 0;
                _pressedButtons |= 1 << button;
                _button = 0;
                if (wasPressed || modifiers == KeyModifiers.None ||
                    !bindings.ContainsKey(new(DragKind(button), modifiers))) return null;
                _button = button;
                _startX = data.X;
                _startY = data.Y;
                _modifiers = modifiers;
                _dragging = false;
                return null;
            }

            var released = button != 0 && message is 0x0202 or 0x0205 or 0x0208 or 0x020C;
            if (released) _pressedButtons &= ~(1 << button);
            if (_button == 0 || (message != 0x0200 && !released)) return null;
            if (Math.Abs((long)data.X - _startX) > dragWidth || Math.Abs((long)data.Y - _startY) > dragHeight)
                _dragging = true;
            if (!released || button != _button) return null;
            _button = 0;
            if (!_dragging || _pressedButtons != 0) return null;
            var action = bindings.GetValueOrDefault(new(DragKind(button), _modifiers));
            if (action is null) return null;
            var left = Math.Min(_startX, data.X);
            var top = Math.Min(_startY, data.Y);
            var width = Math.Max(1, Math.Abs((long)data.X - _startX));
            var height = Math.Max(1, Math.Abs((long)data.Y - _startY));
            if (width > int.MaxValue || height > int.MaxValue ||
                left + width > int.MaxValue || top + height > int.MaxValue) return null;
            region = new(left, top, (int)width, (int)height);
            return action;
        }

        private static MouseGestureKind DragKind(int button)
        {
            return button switch
            {
                1 => MouseGestureKind.LeftDrag,
                2 => MouseGestureKind.RightDrag,
                3 => MouseGestureKind.MiddleDrag,
                4 => MouseGestureKind.X1Drag,
                5 => MouseGestureKind.X2Drag,
                _ => MouseGestureKind.None,
            };
        }
    }
}
