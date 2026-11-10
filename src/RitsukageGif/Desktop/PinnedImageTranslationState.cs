namespace RitsukageGif.Desktop
{
    internal sealed record PinnedImageTranslationState(
        string TargetLanguage,
        bool Enabled,
        bool Empty,
        PinnedImageTranslationCache? Cache)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        internal bool IsValid => TargetLanguage is not null && Cache is not { IsValid: false };
    }
}
