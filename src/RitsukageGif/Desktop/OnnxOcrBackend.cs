namespace RitsukageGif.Desktop
{
    internal sealed class OnnxOcrBackend : IPlatformOcrBackend
    {
        public string Id => "onnx-offline";

        public Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            return StructuredRecognitionProcess.Shared.GetTextCapabilitiesAsync(token);
        }

        public Task<OcrRecognitionResult> RecognizeAsync(byte[] pixels, int width, int height,
            string? languageTag, CancellationToken token)
        {
            return StructuredRecognitionProcess.Shared.RecognizeTextAsync(pixels, width, height, languageTag, token);
        }
    }
}
