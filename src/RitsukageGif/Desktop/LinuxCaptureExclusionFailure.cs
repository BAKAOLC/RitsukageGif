namespace RitsukageGif.Desktop
{
    internal enum LinuxCaptureExclusionFailure
    {
        None,
        SourceDoesNotSupportExclusion,
        MissingHelper,
        CompositorUnavailable,
        ProducerChanged,
        ExclusionNotAcknowledged,
        ReleaseNotAcknowledged,
    }
}
