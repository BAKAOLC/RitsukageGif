namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureCapabilities
    {
        public bool Screen { get; set; }
        public bool Window { get; set; }
        public bool Hdr { get; set; }
        public bool SeparateCursor { get; set; }
        public bool PointerInput { get; set; }
        public bool CanRequestPointerInput { get; set; }
        public uint CursorMode { get; set; }
        public uint AvailableCursorModes { get; set; }
        public bool ProducerChecked { get; set; }
        public uint ProducerPid { get; set; }
        public string? Producer { get; set; }
        public string? Reason { get; set; }
    }
}
