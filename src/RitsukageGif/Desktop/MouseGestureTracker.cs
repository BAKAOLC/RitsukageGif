using Avalonia;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class MouseGestureTracker
    {
        private MouseButton _button;
        private bool _doubleClick;
        private KeyModifiers _modifiers;
        internal bool IsActive { get; private set; }
        private bool IsDragging { get; set; }
        private Point Start { get; set; }

        internal bool Begin(MouseButton button, KeyModifiers modifiers, Point point, int clickCount)
        {
            if (IsActive || button is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle
                    or MouseButton.XButton1 or MouseButton.XButton2)) return false;
            IsActive = true;
            IsDragging = false;
            _button = button;
            _modifiers = modifiers;
            _doubleClick = clickCount >= 2;
            Start = point;
            return true;
        }

        internal bool Move(Point point, double threshold, out InputMouseGesture gesture)
        {
            gesture = default;
            if (!IsActive || IsDragging ||
                (Math.Abs(point.X - Start.X) <= threshold && Math.Abs(point.Y - Start.Y) <= threshold)) return false;
            IsDragging = true;
            gesture = new(_button switch
            {
                MouseButton.Left => MouseGestureKind.LeftDrag,
                MouseButton.Right => MouseGestureKind.RightDrag,
                MouseButton.XButton1 => MouseGestureKind.X1Drag,
                MouseButton.XButton2 => MouseGestureKind.X2Drag,
                _ => MouseGestureKind.MiddleDrag,
            }, _modifiers);
            return true;
        }

        internal bool End(MouseButton button, out InputMouseGesture gesture)
        {
            gesture = default;
            if (!IsActive || button != _button) return false;
            var discrete = !IsDragging;
            if (discrete)
                gesture = new((_button, _doubleClick) switch
                {
                    (MouseButton.Left, false) => MouseGestureKind.LeftClick,
                    (MouseButton.Left, true) => MouseGestureKind.LeftDoubleClick,
                    (MouseButton.Right, false) => MouseGestureKind.RightClick,
                    (MouseButton.Right, true) => MouseGestureKind.RightDoubleClick,
                    (MouseButton.Middle, false) => MouseGestureKind.MiddleClick,
                    (MouseButton.XButton1, false) => MouseGestureKind.X1Click,
                    (MouseButton.XButton1, true) => MouseGestureKind.X1DoubleClick,
                    (MouseButton.XButton2, false) => MouseGestureKind.X2Click,
                    (MouseButton.XButton2, true) => MouseGestureKind.X2DoubleClick,
                    _ => MouseGestureKind.MiddleDoubleClick,
                }, _modifiers);
            Reset();
            return discrete;
        }

        internal void Reset()
        {
            IsActive = false;
            IsDragging = false;
        }
    }
}
