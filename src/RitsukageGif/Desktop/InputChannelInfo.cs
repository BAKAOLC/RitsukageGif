namespace RitsukageGif.Desktop
{
    internal sealed record InputChannelInfo(
        string Name,
        string Transport,
        string Endpoint,
        string Format,
        bool Connected,
        long Packets,
        double? Timestamp,
        string? Error,
        ExternalInputChannelStatus? Status);
}
