using System;
using RitsukageGif.Class;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public static class ImageEncoderFactory
    {
        public static IImageEncoder CreateEncoder(OutputFormat format, string filePath)
        {
            var useFfmpeg = Settings.Default.UseFfmpegEncoder && FfmpegHelper.IsAvailable;

            return format switch
            {
                OutputFormat.Gif => useFfmpeg
                    ? new FfmpegGifEncoder(filePath)
                    : new GifImageEncoder(filePath),
                OutputFormat.WebP => useFfmpeg
                    ? new FfmpegWebPEncoder(filePath)
                    : new WebPImageEncoder(filePath),
                OutputFormat.APng => useFfmpeg
                    ? new FfmpegAPngEncoder(filePath)
                    : new APngImageEncoder(filePath),
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