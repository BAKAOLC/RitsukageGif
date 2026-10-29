using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed class EditorStyles : Styles
    {
        public EditorStyles()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
