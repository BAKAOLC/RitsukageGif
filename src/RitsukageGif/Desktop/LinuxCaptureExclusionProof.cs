namespace RitsukageGif.Desktop
{
    internal readonly record struct LinuxCaptureExclusionProof(
        LinuxCaptureExclusionSource Source,
        uint ProducerPid = 0);
}
