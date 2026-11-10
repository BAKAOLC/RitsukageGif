namespace RitsukageGif.Desktop
{
    internal sealed record RecordingOutcome(bool Cancelled = false, Exception? Error = null)
    {
        internal IReadOnlyList<string> Outputs { get; init; } = [];
        internal bool SourceLost { get; init; }
        internal TimeSpan RecordedDuration { get; init; }
        internal long AcceptedCaptures { get; init; }

        public void EnsureCompleted()
        {
            if (Error is not null) throw new InvalidOperationException(Error.Message, Error);
            if (Cancelled) throw new InvalidOperationException(UiText.Get("Cancelled"));
        }
    }
}
