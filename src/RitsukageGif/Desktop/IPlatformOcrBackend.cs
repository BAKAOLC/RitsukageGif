namespace RitsukageGif.Desktop
{
    internal interface IPlatformOcrBackend
    {
        string Id { get; }

        Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token);

        Task<OcrRecognitionResult> RecognizeAsync(byte[] pixels, int width, int height, string? languageTag,
            CancellationToken token);
    }
}
