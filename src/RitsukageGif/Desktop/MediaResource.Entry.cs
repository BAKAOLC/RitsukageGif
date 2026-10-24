namespace RitsukageGif.Desktop
{
    internal sealed partial class MediaResource
    {
        private sealed class Entry(string path, long length, DateTimeOffset expires)
        {
            public string Path { get; } = path;
            public long Length { get; } = length;
            public DateTimeOffset Expires { get; } = expires;
            public long LastUsed { get; set; } = Environment.TickCount64;
            public int References { get; set; } = 1;
            public bool CacheConsidered { get; set; }
        }
    }
}
