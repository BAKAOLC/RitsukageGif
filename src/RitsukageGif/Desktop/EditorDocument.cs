using AvaloniaEdit.Document;

namespace RitsukageGif.Desktop
{
    internal sealed class EditorDocument(string? path, string text)
    {
        public string? Path { get; set; } = path;
        public TextDocument Buffer { get; } = new(text);
        public string SavedText { get; set; } = text;
        public string? ObservedText { get; set; } = text;
        public bool Conflict { get; set; }
        public int Caret { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public List<TextAnchor> Breakpoints { get; } = [];
        public bool Dirty => Buffer.Text != SavedText;
    }
}
