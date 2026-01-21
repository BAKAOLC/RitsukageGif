using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public class GifImageEncoder(string filePath) : IImageEncoder
    {
        private readonly Stream _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        private bool _hasFrames;
        private Image<Rgba32>? _image;

        public async Task AddFrameAsync(string path, int delayMs, CancellationToken token)
        {
            using var frame = await Image.LoadAsync<Rgba32>(path, token).ConfigureAwait(false);

            if (!_hasFrames)
            {
                _image = new(frame.Width, frame.Height);
                var metadata = _image.Metadata.GetGifMetadata();
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
                var newFrame = _image!.Frames.CreateFrame();
                frame.Frames.RootFrame.ProcessPixelRows(newFrame, (source, target) =>
                {
                    for (var i = 0; i < source.Height; i++) source.GetRowSpan(i).CopyTo(target.GetRowSpan(i));
                });
            }

            var frameMetadata = _image.Frames[^1].Metadata.GetGifMetadata();
            frameMetadata.FrameDelay = delayMs / 10;
            frameMetadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
        }

        public Task FinalizeAsync(CancellationToken token)
        {
            if (_hasFrames && _image != null)
            {
                var encoder = new GifEncoder
                {
                    ColorTableMode = GifColorTableMode.Global,
                };
                _image.SaveAsGif(_stream, encoder);
            }

            _stream.Dispose();
            _image?.Dispose();

            return Task.CompletedTask;
        }
    }
}