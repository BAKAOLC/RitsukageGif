namespace RitsukageGif.Desktop
{
    internal sealed record ImageEditorStoredDocument(
        string PngPath,
        string? HdrPath,
        CapturedFrame? HdrMetadata,
        long ByteLength);
}
