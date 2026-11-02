namespace RitsukageGif.Desktop
{
    internal readonly record struct WindowRoiCaptureIdentity(
        long Handle,
        uint ProcessId,
        bool Cursor,
        NativeCaptureAuthorization? Authorization,
        PixelRect? Region)
    {
        internal static WindowRoiCaptureIdentity From(WindowRoiPinDescriptor descriptor)
        {
            return new(descriptor.Handle, descriptor.ProcessId, descriptor.Cursor, descriptor.NativeAuthorization,
                OperatingSystem.IsMacOS() || descriptor.NativeAuthorization is not null
                    ? descriptor.RelativeRegion
                    : null);
        }
    }
}
