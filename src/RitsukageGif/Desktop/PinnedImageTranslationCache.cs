using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedImageTranslationCache(
        string ImageId,
        int Width,
        int Height,
        string SourceLanguage,
        string TargetLanguage,
        string TranslatedText,
        ImageTranslationBlock[] Blocks)
    {
        [JsonIgnore] public PinnedImageTranslationRaster? Raster { get; init; }

        // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, RedundantAlwaysMatchSubpattern
        internal bool IsValid => Guid.TryParseExact(ImageId, "N", out _) && Width > 0 && Height > 0 &&
                                 SourceLanguage is not null && TargetLanguage is not null &&
                                 TranslatedText is not null &&
                                 (long)Width * Height <= 128L * 1024 * 1024 && Blocks is not null && Blocks.All(block =>
                                     block is { TranslatedText: not null, Words: not null } &&
                                     float.IsFinite(block.Bounds.X) && float.IsFinite(block.Bounds.Y) &&
                                     float.IsFinite(block.Bounds.Width) && float.IsFinite(block.Bounds.Height) &&
                                     block.Bounds is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 } &&
                                     block.Bounds.X + block.Bounds.Width <= Width &&
                                     block.Bounds.Y + block.Bounds.Height <= Height);
        // ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, RedundantAlwaysMatchSubpattern

        internal long EstimatedBytes => 2L * (ImageId.Length + SourceLanguage.Length + TargetLanguage.Length +
                                              TranslatedText.Length) + Blocks.Sum(block =>
            2L * block.TranslatedText.Length + 16L * block.Words.Count + 32) + (Raster is { Content: var content }
            ? (long)content.Png.Length + content.Pixels.Length + content.ThumbnailPng.Length +
              (content.SdrPng?.Length ?? 0) + (content.Hdr?.Pixels.Length ?? 0)
            : 0);

        internal ImageTranslationDocument Document(ReadOnlyMemory<byte> sourcePng)
        {
            return new(sourcePng, Width, Height, SourceLanguage, TargetLanguage, TranslatedText, Blocks);
        }
    }
}
