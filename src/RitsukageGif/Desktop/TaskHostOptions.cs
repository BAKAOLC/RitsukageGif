namespace RitsukageGif.Desktop
{
    internal sealed record TaskHostOptions(string SessionId, bool KeepAlive = false, int? ParentProcessId = null);
}
