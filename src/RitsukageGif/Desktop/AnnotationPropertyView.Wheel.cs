using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private long _lastChoiceWheelAt = Environment.TickCount64 - 40;

        private void BindChoiceWheel<T>(Control control, T[] choices, Func<T> current, Func<T, Task> update)
            where T : struct, Enum
        {
            control.PointerWheelChanged += async (_, args) =>
            {
                args.Handled = true;
                if (_disposed) return;
                var now = Environment.TickCount64;
                if (now - _lastChoiceWheelAt < 40) return;
                _lastChoiceWheelAt = now;
                var delta = args.Delta.Y != 0 ? args.Delta.Y : args.Delta.X;
                if (delta == 0) return;
                var index = Array.IndexOf(choices, current());
                var next = Math.Clamp(index + (delta > 0 ? -1 : 1), 0, choices.Length - 1);
                if (next != index) await update(choices[next]);
            };
        }
    }
}
