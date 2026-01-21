using System;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public static class ImageEncoderFactory
    {
        public static IImageEncoder CreateEncoder(OutputFormat format, string filePath)
        {
            return format switch
            {
                OutputFormat.Gif => new GifImageEncoder(filePath),
                OutputFormat.WebP => new WebPImageEncoder(filePath),
                OutputFormat.APng => new APngImageEncoder(filePath),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };
        }

        public static string GetFileExtension(OutputFormat format)
        {
            return format switch
            {
                OutputFormat.Gif => ".gif",
                OutputFormat.WebP => ".webp",
                OutputFormat.APng => ".png",
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };
        }
    }
}