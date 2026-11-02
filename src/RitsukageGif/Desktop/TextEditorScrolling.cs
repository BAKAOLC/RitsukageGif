using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace RitsukageGif.Desktop
{
    internal static class TextEditorScrolling
    {
        internal static void Restore(TextEditor editor, Vector offset)
        {
            editor.ApplyTemplate();
            editor.UpdateLayout();
            if (editor.TextArea.TextView.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                scroll.Offset = offset;
        }
    }
}
