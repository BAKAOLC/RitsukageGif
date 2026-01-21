using System.Drawing;
using System.Threading;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public interface IRecordFrameProvider
    {
        string GetFileExtension();

        RecordInfo BeginWithMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken,
            OutputFormat format = OutputFormat.Gif);

        RecordInfo BeginWithoutMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken,
            OutputFormat format = OutputFormat.Gif);
    }
}