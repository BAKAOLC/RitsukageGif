using System;
using System.Threading;
using System.Threading.Tasks;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public interface IImageEncoder
    {
        Task AddFrameAsync(string path, int delayMs, CancellationToken token);
        Task FinalizeAsync(CancellationToken token);
    }
}