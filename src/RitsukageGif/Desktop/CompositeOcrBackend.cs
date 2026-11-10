namespace RitsukageGif.Desktop
{
    internal sealed class CompositeOcrBackend(IPlatformOcrBackend platform) : IPlatformOcrBackend
    {
        private readonly OnnxOcrBackend _portable = new();

        public string Id => "offline+" + platform.Id;

        public async Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            var portable = await _portable.GetCapabilitiesAsync(token).ConfigureAwait(false);
            var native = await platform.GetCapabilitiesAsync(token).ConfigureAwait(false);
            if (!portable.Available) return native;
            var languages = portable.Languages.Concat(native.Languages.Where(language =>
                !Supports(portable, language.Tag))).ToArray();
            return new(true, Math.Max(portable.MaxImageDimension, native.MaxImageDimension),
                Array.AsReadOnly(languages), null);
        }

        public async Task<OcrRecognitionResult> RecognizeAsync(byte[] pixels, int width, int height,
            string? languageTag, CancellationToken token)
        {
            var portable = await _portable.GetCapabilitiesAsync(token).ConfigureAwait(false);
            if (portable.Available && (string.IsNullOrWhiteSpace(languageTag) || Supports(portable, languageTag)))
                return await _portable.RecognizeAsync(pixels, width, height, languageTag, token).ConfigureAwait(false);
            return await platform.RecognizeAsync(pixels, width, height, languageTag, token).ConfigureAwait(false);
        }

        private static bool Supports(OcrCapabilities capabilities, string tag)
        {
            return capabilities.Languages.Any(language =>
                language.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith(language.Tag + "-", StringComparison.OrdinalIgnoreCase));
        }
    }
}
