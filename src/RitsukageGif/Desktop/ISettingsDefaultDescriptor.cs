using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal interface ISettingsDefaultDescriptor
    {
        Control Label { get; }
        bool IsChanged(AppSettings settings);
    }
}
