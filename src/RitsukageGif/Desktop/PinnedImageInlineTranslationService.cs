namespace RitsukageGif.Desktop
{
    internal static class PinnedImageInlineTranslationService
    {
        internal static async Task<PinnedImageTranslationCache?> TranslateAsync(ReadOnlyMemory<byte> sourcePng,
            CapturedFrame? sourceHdr, TranslationPreferences preferences, RecognitionPreferences recognition,
            ITextRecognitionService ocr, string apiKey, CancellationToken token, HttpClient? client = null)
        {
            preferences = preferences with { Mode = TranslationMode.ImageToImage };
            var recognized =
                await ImageTranslationService.RecognizeAsync(sourcePng, preferences, recognition, ocr, token);
            if (recognized.Lines.All(line => string.IsNullOrWhiteSpace(line.Text))) return null;
            var document = await new ImageTranslationService(new(client))
                .TranslateRecognizedAsync(sourcePng, recognized, preferences, apiKey, token);
            if (document.Blocks.Count == 0) return null;
            var background =
                await Task.Run(() => ImageTranslationBackground.Create(document, token, sourceHdr is not null), token);
            var rendered =
                await ImageTranslationRenderer.RenderAsync(document, background, token, sourceHdr is not null);
            var content = await Task.Run(() => PinnedImageContent.Decode(rendered.Png, token), token);
            if (sourceHdr is not null)
            {
                var hdr = await Task.Run(() => ImageTranslationHdrRenderer.Compose(sourceHdr, background.InpaintRegions,
                    rendered.TextOverlay!, token), token);
                var hdrPng = await HdrImageService.EncodePngAsync(hdr, token);
                content = PinnedImageContent.FromHdr(hdrPng, content, hdr);
            }

            token.ThrowIfCancellationRequested();
            return new(Guid.NewGuid().ToString("N"), document.Width, document.Height, document.SourceLanguage,
                document.TargetLanguage, document.TranslatedText, [.. document.Blocks])
            {
                Raster = new(content, rendered.Text),
            };
        }
    }
}
