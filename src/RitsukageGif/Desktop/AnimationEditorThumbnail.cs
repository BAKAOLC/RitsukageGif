using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnail : Image
    {
        private static readonly SemaphoreSlim DecodeSlots = new(2);
        private readonly string _path;
        private Bitmap? _bitmap;
        private CancellationTokenSource? _loading;
        private int _generation;

        public AnimationEditorThumbnail(string path)
        {
            _path = path;
            Width = 76;
            Height = 48;
            Stretch = Stretch.Uniform;
        }

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var generation = ++_generation;
            using var loading = new CancellationTokenSource();
            _loading = loading;
            var token = loading.Token;
            try
            {
                await DecodeSlots.WaitAsync(token);
                byte[] bytes;
                try
                {
                    bytes = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        using var image = new MagickImage();
                        image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                        image.Read(_path);
                        token.ThrowIfCancellationRequested();
                        image.Thumbnail(76, 48);
                        token.ThrowIfCancellationRequested();
                        return image.ToByteArray(MagickFormat.Png);
                    }, token);
                }
                finally
                {
                    DecodeSlots.Release();
                }

                if (generation != _generation || token.IsCancellationRequested) return;
                using var stream = new MemoryStream(bytes);
                _bitmap = new Bitmap(stream);
                Source = _bitmap;
            }
            catch (Exception)
            {
                /* Detached or removed frames have no thumbnail to display. */
            }
            finally
            {
                if (ReferenceEquals(_loading, loading)) _loading = null;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _generation++;
            _loading?.Cancel();
            Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            base.OnDetachedFromVisualTree(e);
        }
    }
}
