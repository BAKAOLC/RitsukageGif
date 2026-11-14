namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly AnnotationToolsStrip _annotationTools;

        private void RebuildToolColumns(ToolbarPreferences preferences)
        {
            _annotationTools.Rebuild(preferences);
        }

        private void HideToolGroups()
        {
            _annotationTools.HideFlyouts();
        }
    }
}
