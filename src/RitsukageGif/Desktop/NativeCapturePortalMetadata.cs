namespace RitsukageGif.Desktop
{
    internal sealed class NativeCapturePortalMetadata
    {
        public string? Id { get; set; }
        public ulong Serial { get; set; }
        public uint Type { get; set; }
        public int[]? Position { get; set; }
        public int[]? Size { get; set; }
        public int[]? LogicalSize { get; set; }
        public uint Version { get; set; }
        public string? RestoreToken { get; set; }
    }
}
