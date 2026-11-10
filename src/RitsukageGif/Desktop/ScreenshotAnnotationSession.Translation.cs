namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal event Action? TranslationRequested;

        internal void RequestTranslation()
        {
            CommitTextEdit();
            if (CanExport && CanAnnotate) TranslationRequested?.Invoke();
        }
    }
}
