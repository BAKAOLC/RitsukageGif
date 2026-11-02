namespace RitsukageGif.Desktop
{
    internal static class ToolbarActionCatalog
    {
        internal static IReadOnlyList<ToolbarActionDefinition> All { get; } =
        [
            new(ToolbarActionId.Undo, "AnimationUndo", "AnimationUndo", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Undo),
            new(ToolbarActionId.ScrollCapture, "ScreenshotScroll", "ScreenshotScroll", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.ScrollVertical),
            new(ToolbarActionId.Record, "ScreenshotRecord", "ScreenshotRecord", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Record),
            new(ToolbarActionId.Pin, "ScreenshotPin", "ScreenshotPin", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Pin),
            new(ToolbarActionId.Save, "ScreenshotSave", "ScreenshotSave", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Save),
            new(ToolbarActionId.Cancel, "Cancel", "Cancel", ToolbarButtonVisibility.Toolbar, SelectionCommand.Cancel),
            new(ToolbarActionId.Copy, "ScreenshotCopy", "ScreenshotCopy", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Copy),
            new(ToolbarActionId.Redo, "AnimationRedo", "AnimationRedo", ToolbarButtonVisibility.Hidden,
                SelectionCommand.Redo),
            new(ToolbarActionId.DeleteAnnotation, "ScreenshotDeleteAnnotation", "ScreenshotDeleteAnnotation",
                ToolbarButtonVisibility.Hidden, SelectionCommand.Clear),
            new(ToolbarActionId.ToggleAnnotations, "AnnotationToggleVisibility", "AnnotationToggleVisibility",
                ToolbarButtonVisibility.Hidden, SelectionCommand.ToggleAnnotations),
            new(ToolbarActionId.ClearAnnotations, "AnnotationClearAll", "AnnotationClearAll",
                ToolbarButtonVisibility.Hidden, SelectionCommand.ClearAnnotations),
            new(ToolbarActionId.RenumberAnnotations, "AnnotationRenumber", "AnnotationRenumber",
                ToolbarButtonVisibility.Hidden, SelectionCommand.RenumberAnnotations),
            new(ToolbarActionId.RecognizeText, "RecognizeText", "RecognizeText", ToolbarButtonVisibility.MoreMenu,
                SelectionCommand.RecognizeText),
            new(ToolbarActionId.RecognizeBarcode, "RecognizeBarcode", "RecognizeBarcode",
                ToolbarButtonVisibility.Hidden, SelectionCommand.RecognizeQr),
            new(ToolbarActionId.CopyCodeText, "CaptureCodeCopyText", "RecognizeBarcode",
                ToolbarButtonVisibility.Hidden, SelectionCommand.CopyCodeText),
            new(ToolbarActionId.OutputColor, "OutputColorMode", "OutputColorMode", ToolbarButtonVisibility.Hidden),
            new(ToolbarActionId.Translate, "TranslationRun", "TranslationRun", ToolbarButtonVisibility.MoreMenu,
                SelectionCommand.Translate),
            new(ToolbarActionId.QuickSave, "ScreenshotQuickSave", "ScreenshotSave", ToolbarButtonVisibility.MoreMenu,
                SelectionCommand.QuickSave),
            new(ToolbarActionId.RecognizeTable, "TableRecognition", "TableRecognition",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.RecognizeTable),
            new(ToolbarActionId.RecognizeFormula, "FormulaRecognition", "FormulaRecognition",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.RecognizeFormula),
            new(ToolbarActionId.ExportPdf, "DocumentExport", "DocumentExport", ToolbarButtonVisibility.MoreMenu),
            new(ToolbarActionId.Print, "Print", "Print", ToolbarButtonVisibility.Hidden, SelectionCommand.Print),
            new(ToolbarActionId.WindowPin, "ScreenshotWindowPin", "ScreenshotWindowPin",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.WindowPin),
            new(ToolbarActionId.Edit, "ImageEditorEdit", "AnimationBrush", ToolbarButtonVisibility.Hidden),
        ];

        public static ToolbarActionDefinition Get(ToolbarActionId id)
        {
            return All.First(definition => definition.Id == id);
        }
    }
}
