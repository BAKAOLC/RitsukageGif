namespace RitsukageGif.Desktop
{
    internal static class ToolbarActionCatalog
    {
        private static IReadOnlyList<ToolbarActionDefinition> All { get; } =
        [
            new(ToolbarActionId.Undo, "AnimationUndo", "AnimationUndo"),
            new(ToolbarActionId.ScrollCapture, "ScreenshotScroll", "ScreenshotScroll"),
            new(ToolbarActionId.Pin, "ScreenshotPin", "ScreenshotPin"),
            new(ToolbarActionId.Save, "ScreenshotSave", "ScreenshotSave"),
            new(ToolbarActionId.Copy, "ScreenshotCopy", "ScreenshotCopy"),
            new(ToolbarActionId.Cancel, "Cancel", "Cancel"),
            new(ToolbarActionId.Redo, "AnimationRedo", "AnimationRedo"),
            new(ToolbarActionId.DeleteAnnotation, "ScreenshotDeleteAnnotation", "ScreenshotDeleteAnnotation"),
            new(ToolbarActionId.ToggleAnnotations, "AnnotationToggleVisibility", "AnnotationToggleVisibility"),
            new(ToolbarActionId.ClearAnnotations, "AnnotationClearAll", "AnnotationClearAll"),
            new(ToolbarActionId.RenumberAnnotations, "AnnotationRenumber", "AnnotationRenumber"),
            new(ToolbarActionId.RecognizeText, "RecognizeText", "RecognizeText"),
            new(ToolbarActionId.RecognizeBarcode, "RecognizeBarcode", "RecognizeBarcode"),
            new(ToolbarActionId.OutputColor, "OutputColorMode", "OutputColorMode"),
        ];

        public static ToolbarActionDefinition Get(ToolbarActionId id)
        {
            return All.First(definition => definition.Id == id);
        }
    }
}
