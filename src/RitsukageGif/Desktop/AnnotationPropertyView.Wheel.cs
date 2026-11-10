using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private void BindChoiceWheel<T>(Control control, T[] choices, Func<T> current, Func<T, Task> update)
            where T : struct, Enum
        {
            control.PointerWheelChanged += async (_, args) =>
            {
                args.Handled = true;
                if (_disposed || !control.IsEffectivelyEnabled) return;
                if (AnnotationChoiceWheel.TryStep(args.Delta, Array.IndexOf(choices, current()), choices.Length,
                        out var next))
                    await update(choices[next]);
            };
        }
    }
}
