namespace RitsukageGif.Desktop
{
    internal sealed record PinnedImageOcrState(
        bool? TextSelectable,
        bool Attempted,
        OcrRecognitionResult? Result)
    {
        // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, RedundantAlwaysMatchSubpattern
        internal bool IsValid => Result is null ||
                                 (Result is { Text: not null, LanguageTag: not null, Lines: not null } &&
                                  Result.Lines.All(line => line is { Text: not null, Words: not null } &&
                                                           line.Words.All(word => word is { Text: not null })));
        // ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, RedundantAlwaysMatchSubpattern

        internal long EstimatedBytes => Result is not null
            ? 2L * (Result.Text.Length + Result.LanguageTag.Length) + Result.Lines.Sum(line =>
                2L * line.Text.Length + 32 + line.Words.Sum(word => 2L * word.Text.Length + 16))
            : 0;
    }
}
