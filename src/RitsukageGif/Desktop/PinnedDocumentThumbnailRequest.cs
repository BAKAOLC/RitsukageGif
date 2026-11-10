namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentThumbnailRequest(bool Enabled, int? ScreenX, int? ScreenY)
    {
        internal bool IsValid => ScreenX.HasValue == ScreenY.HasValue;
    }
}
