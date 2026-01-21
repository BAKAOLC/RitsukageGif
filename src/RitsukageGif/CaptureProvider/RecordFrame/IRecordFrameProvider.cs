using System.Drawing;
using System.Threading;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public interface IRecordFrameProvider
    {
        string GetFileExtension();

        void SetOutputFormat(OutputFormat format);

        RecordInfo BeginWithMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken);

        RecordInfo BeginWithoutMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken);
    }
}