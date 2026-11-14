namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedInputResult(bool Handled, IPinnedWindow? Created)
    {
        internal (PinnedDocument Document, PinnedSourceMime? Source, int Length)? LongText { get; init; }
    }
}
