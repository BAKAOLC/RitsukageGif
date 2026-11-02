namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureMetadata
    {
        public long ConfigurationId { get; set; }
        public NativeCaptureGeometry? SourceGeometry { get; set; }
        public long TimestampNanoseconds { get; set; }
        public NativeCaptureGeometry Geometry { get; set; } = new();
        public NativeCapturePlane? Sdr { get; set; }
        public NativeCapturePlane? Hdr { get; set; }
        public NativeCaptureCursor? Cursor { get; set; }
        public NativeCaptureCapabilities? Capabilities { get; set; }
        public NativeCapturePortalMetadata? Portal { get; set; }
        public NativeCaptureSourceEncoding? Encoding { get; set; }
        public long RequestId { get; set; }
        public string? Result { get; set; }
        public string? Reason { get; set; }
    }
}
