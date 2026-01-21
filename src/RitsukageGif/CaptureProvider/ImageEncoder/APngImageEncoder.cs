using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public class APngImageEncoder(string filePath) : IImageEncoder
    {
        private readonly Stream _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        private bool _disposed;
        private bool _hasFrames;
        private Image<Rgba32> _image;

        public async Task AddFrameAsync(Bitmap bitmap, int delayMs, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            using var frame = await Image.LoadAsync<Rgba32>(memoryStream, cancellationToken).ConfigureAwait(false);

            if (!_hasFrames)
            {
                _image = new(frame.Width, frame.Height);
                var metadata = _image.Metadata.GetPngMetadata();
                metadata.RepeatCount = 0;

                var rootFrame = _image.Frames.RootFrame;
                frame.Frames.RootFrame.ProcessPixelRows(rootFrame, (source, target) =>
                {
                    for (var i = 0; i < source.Height; i++) source.GetRowSpan(i).CopyTo(target.GetRowSpan(i));
                });
                _hasFrames = true;
            }
            else
            {
                var newFrame = _image.Frames.CreateFrame();
                frame.Frames.RootFrame.ProcessPixelRows(newFrame, (source, target) =>
                {
                    for (var i = 0; i < source.Height; i++) source.GetRowSpan(i).CopyTo(target.GetRowSpan(i));
                });
            }

            var frameMetadata = _image.Frames[^1].Metadata.GetPngMetadata();
            frameMetadata.FrameDelay = new((uint)delayMs, 1000);
        }

        public void Finish()
        {
            if (_disposed) return;

            if (_hasFrames && _stream != null && _image != null)
            {
                var encoder = new PngEncoder
                {
                    ColorType = PngColorType.RgbWithAlpha,
                    CompressionLevel = PngCompressionLevel.BestCompression,
                };
                _image.SaveAsPng(_stream, encoder);
            }

            _stream?.Dispose();
            _image?.Dispose();
            _disposed = true;
        }

        public void Dispose()
        {
            Finish();
            GC.SuppressFinalize(this);
        }
    }
}