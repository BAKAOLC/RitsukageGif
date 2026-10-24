namespace RitsukageGif.Core
{
    public sealed record RecordingRetentionPolicy(
        bool Enabled = false,
        int? MaxAgeDays = null,
        int? MaxFiles = null,
        int? MaxSizeMiB = null)
    {
        public bool HasRules => Enabled && (MaxAgeDays is > 0 || MaxFiles is > 0 || MaxSizeMiB is > 0);
    }
}
