namespace RitsukageGif.Desktop
{
    internal sealed record LinuxCaptureExclusionResult(
        LinuxCaptureExclusionCapability Capability,
        LinuxCaptureExclusionFailure Failure = LinuxCaptureExclusionFailure.None,
        LinuxCaptureExclusionLease? Lease = null);
}
