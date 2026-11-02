namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentState(PinnedWindowState Window, PinnedDocument Document)
    {
        public bool IgnoreFormat { get; set; }
        public bool Selectable { get; set; } = true;
        public double Width { get; set; } = 440;
        public double Height { get; set; } = 320;
    }
}
