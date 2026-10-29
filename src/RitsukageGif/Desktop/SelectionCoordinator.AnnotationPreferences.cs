namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal event Action<ToolbarPreferences, bool?>? AnnotationToolbarChanged;

        internal void InitializeAnnotationPreferences(AnnotationPreferences preferences)
        {
            if (_started) throw new InvalidOperationException();
            _autoSelectPrivacyMosaic = preferences.AutoSelectPrivacyMosaic;
            Annotations?.ApplyPreferences(preferences);
        }

        internal AnnotationPreferences CaptureAnnotationPreferences(AnnotationPreferences preferences)
        {
            return Annotations?.CapturePreferences(preferences) ?? preferences;
        }
    }
}
