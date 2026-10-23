namespace RitsukageGif.Desktop
{
    internal sealed record RecordingOutcome(bool Cancelled = false, Exception? Error = null)
    {
        public void EnsureCompleted()
        {
            if (Error is not null) throw new InvalidOperationException(Error.Message, Error);
            if (Cancelled) throw new InvalidOperationException(UiText.Get("Cancelled"));
        }
    }
}
