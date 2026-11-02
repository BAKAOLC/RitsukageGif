using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal interface IPinnedDocumentTextView
    {
        Control Control { get; }
        string SelectedText { get; }
        string? SelectedHtml { get; }
        Vector ScrollOffset { get; set; }
        event EventHandler? ScrollOffsetChanged;
        void SetSelectable(bool value);
        void SelectAll();
        void ReleaseContent();
    }
}
