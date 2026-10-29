using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed record AnnotationPropertyDefinition(string Key, Func<AnnotationPropertyView, Control> Create);
}
