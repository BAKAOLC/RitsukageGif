using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class SettingsDefaultDescriptor<T>(
        Control label,
        Func<AppSettings, T> getter,
        T defaultValue,
        Func<T, T, bool>? equals) : ISettingsDefaultDescriptor
    {
        public Control Label { get; } = label;

        public bool IsChanged(AppSettings settings)
        {
            return !(equals?.Invoke(getter(settings), defaultValue) ??
                     EqualityComparer<T>.Default.Equals(getter(settings), defaultValue));
        }
    }
}
