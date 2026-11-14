using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class GlobalMouseGestureTracker(
        IReadOnlyDictionary<InputMouseGesture, string> bindings,
        int dragWidth,
        int dragHeight)
    {
        internal const uint ModifiersChanged = 0x0100;

        private const int ModifierWindowMilliseconds = 30_000;

        private int _button;
        private bool _dragging;
        private bool _ignoredUntilModifierLeave;
        private KeyModifiers _modifiers;
        private int _pressedButtons;
        private int _startX, _startY;
        private long? _windowsModifierDeadline;
        private bool _windowsModifiersMatched;

        internal bool HasCandidate => _button != 0;
        internal bool WindowsGestureArmed => _windowsModifierDeadline.HasValue;
        internal PixelRect? PreviewRegion { get; private set; }
        internal string? PreviewAction { get; private set; }

        internal string? Process(uint message, GlobalMouseHookData data, KeyModifiers modifiers,
            out PixelRect? region)
        {
            region = null;
            if (message == 0x001F)
            {
                _pressedButtons = 0;
                _ignoredUntilModifierLeave = false;
                CancelCandidate();
                return null;
            }

            if ((data.Flags & 3) != 0) return null;
            if (message == ModifiersChanged)
            {
                if (HasBoundModifiers(modifiers)) return null;
                _ignoredUntilModifierLeave = false;
                return HasCandidate ? Finish(data, out region) : null;
            }

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
                if (wasPressed || _ignoredUntilModifierLeave || modifiers == KeyModifiers.None ||
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
            if (_button == 0) return null;
            if (released) return button == _button ? Finish(data, out region) : null;
            if (message != 0x0200) return null;
            if (!_dragging && Math.Abs((long)data.X - _startX) <= dragWidth &&
                Math.Abs((long)data.Y - _startY) <= dragHeight)
                return null;

            _dragging = true;
            PreviewRegion = Region(data);
            PreviewAction = bindings.GetValueOrDefault(new(DragKind(_button), _modifiers));
            return null;
        }

        internal bool ObserveWindowsModifiers(long now, KeyModifiers modifiers)
        {
            var matches = HasBoundModifiers(modifiers);
            if (matches != _windowsModifiersMatched)
            {
                _windowsModifiersMatched = matches;
                _windowsModifierDeadline = matches ? now + ModifierWindowMilliseconds : null;
                return !matches;
            }

            if (_windowsModifierDeadline is not { } deadline || now < deadline) return false;
            CloseWindowsModifierWindow();
            return true;
        }

        internal void CloseWindowsModifierWindow()
        {
            _windowsModifierDeadline = null;
        }

        internal bool HasBoundModifiers(KeyModifiers modifiers)
        {
            if (modifiers == KeyModifiers.None) return false;
            foreach (var gesture in bindings.Keys)
                if (gesture.Modifiers == modifiers)
                    return true;
            return false;
        }

        internal void IgnorePointerProgram()
        {
            _ignoredUntilModifierLeave = true;
            CancelCandidate();
        }

        private void CancelCandidate()
        {
            _button = 0;
            _dragging = false;
            PreviewRegion = null;
            PreviewAction = null;
        }

        private string? Finish(GlobalMouseHookData data, out PixelRect? region)
        {
            var button = _button;
            var dragging = _dragging;
            CancelCandidate();
            region = null;
            if (!dragging) return null;
            var action = bindings.GetValueOrDefault(new(DragKind(button), _modifiers));
            if (action is null) return null;
            region = Region(data);
            return region is null ? null : action;
        }

        private PixelRect? Region(GlobalMouseHookData data)
        {
            var left = Math.Min(_startX, data.X);
            var top = Math.Min(_startY, data.Y);
            var width = Math.Max(1, Math.Abs((long)data.X - _startX));
            var height = Math.Max(1, Math.Abs((long)data.Y - _startY));
            return width > int.MaxValue || height > int.MaxValue ||
                   left + width > int.MaxValue || top + height > int.MaxValue
                ? null
                : new(left, top, (int)width, (int)height);
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
