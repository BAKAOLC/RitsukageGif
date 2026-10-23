namespace RitsukageGif.Desktop
{
    internal sealed record InputChannelOptions(
        string Name,
        string Transport = "pipe",
        string? Endpoint = null,
        string Format = "framed",
        int Width = 0,
        int Height = 0,
        int IdleTimeoutSeconds = 30);
}
