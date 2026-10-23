namespace RitsukageGif.Core
{
    public sealed record ExternalInputChannelStatus(
        string Name,
        bool Connected,
        int BufferedFrames,
        long BufferedBytes,
        long PublishedFrames,
        long DroppedFrames,
        long FrameVersion,
        long DataVersion,
        double? LastFrameTimestamp,
        double? LastDataTimestamp);
}
