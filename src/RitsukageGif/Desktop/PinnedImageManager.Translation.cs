namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private async Task WriteTranslationImageAsync(PinnedImageTranslationState? translation,
            List<string> created, CancellationToken token)
        {
            if (translation?.Cache is not { Raster: { } raster } cache) return;
            var path = ImagePath(cache.ImageId);
            if (!File.Exists(path)) await WriteSessionImageAsync(path, raster.Content.Png, created, token);
        }

        private async Task<PinnedImageTranslationState?> ReadTranslationImageAsync(
            PinnedImageTranslationState? translation, ReadOnlyMemory<byte> sourcePng, CancellationToken token)
        {
            if (translation?.Cache is not { } cache) return translation;
            var path = ImagePath(cache.ImageId);
            if (!File.Exists(path)) return translation with { Cache = null };
            var encoded = await ReadOriginalBytesAsync(path, token);
            var content = await Task.Run(() => PinnedImageContent.Decode(encoded, token), token);
            if (content.Width != cache.Width || content.Height != cache.Height)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var selection = ImageTranslationRenderer.BuildSelectionDocument(cache.Document(sourcePng), token);
            return translation with { Cache = cache with { Raster = new(content, selection) } };
        }
    }
}
