using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public interface IImageEncoder : IDisposable
    {
        Task AddFrameAsync(Bitmap bitmap, int delayMs, CancellationToken cancellationToken = default);
        void Finish();
    }
}