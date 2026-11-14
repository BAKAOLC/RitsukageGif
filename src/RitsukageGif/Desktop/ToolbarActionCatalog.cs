namespace RitsukageGif.Desktop
{
    internal static class ToolbarActionCatalog
    {
        private static IReadOnlyList<ToolbarActionDefinition> All { get; } =
        [
            new(ToolbarActionId.ScrollCapture, "ScreenshotScroll", "ScreenshotScroll", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.ScrollVertical),
            new(ToolbarActionId.Record, "ScreenshotRecord", "ScreenshotRecord", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Record),
            new(ToolbarActionId.Pin, "ScreenshotPin", "ScreenshotPin", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Pin),
            new(ToolbarActionId.Save, "CaptureActionSaveImage", "ScreenshotSave", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Save),
            new(ToolbarActionId.Cancel, "Close", "Cancel", ToolbarButtonVisibility.Toolbar, SelectionCommand.Cancel),
            new(ToolbarActionId.Copy, "CaptureActionCopyAndClose", "ScreenshotCopy", ToolbarButtonVisibility.Toolbar,
                SelectionCommand.Copy),
            new(ToolbarActionId.RecognizeTable, "CaptureActionTableRecognition", "TableRecognition",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.RecognizeTable),
            new(ToolbarActionId.QuickSave, "ScreenshotQuickSave", "ScreenshotQuickSave",
                ToolbarButtonVisibility.MoreMenu,
                SelectionCommand.QuickSave),
            new(ToolbarActionId.RecognizeFormula, "CaptureActionFormulaRecognition", "FormulaRecognition",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.RecognizeFormula),
            new(ToolbarActionId.WindowPin, "ScreenshotWindowPin", "ScreenshotWindowPin",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.WindowPin),
            new(ToolbarActionId.RecognizeText, "CaptureActionCopyOcrText", "RecognizeText",
                ToolbarButtonVisibility.MoreMenu, SelectionCommand.RecognizeTextAndCopy),
            new(ToolbarActionId.Translate, "TranslationRun", "TranslationRun", ToolbarButtonVisibility.MoreMenu,
                SelectionCommand.Translate),
            new(ToolbarActionId.Print, "Print", "Print", ToolbarButtonVisibility.Hidden, SelectionCommand.Print),
            new(ToolbarActionId.Edit, "ImageEditorEdit", "ScreenshotImageEdit", ToolbarButtonVisibility.Hidden),
            new(ToolbarActionId.Undo, "AnimationUndo", "AnimationUndo", ToolbarButtonVisibility.Hidden,
                SelectionCommand.Undo, false),
            new(ToolbarActionId.Redo, "AnimationRedo", "AnimationRedo", ToolbarButtonVisibility.Hidden,
                SelectionCommand.Redo, false),
            new(ToolbarActionId.DeleteAnnotation, "ScreenshotDeleteAnnotation", "ScreenshotDeleteAnnotation",
                ToolbarButtonVisibility.Hidden, SelectionCommand.Clear, false),
            new(ToolbarActionId.ToggleAnnotations, "AnnotationToggleVisibility", "AnnotationToggleVisibility",
                ToolbarButtonVisibility.Hidden, SelectionCommand.ToggleAnnotations, false),
            new(ToolbarActionId.ClearAnnotations, "AnnotationClearAll", "AnnotationClearAll",
                ToolbarButtonVisibility.Hidden, SelectionCommand.ClearAnnotations, false),
            new(ToolbarActionId.RenumberAnnotations, "AnnotationRenumber", "AnnotationRenumber",
                ToolbarButtonVisibility.Hidden, SelectionCommand.RenumberAnnotations, false),
            new(ToolbarActionId.RecognizeBarcode, "RecognizeBarcode", "RecognizeBarcode",
                ToolbarButtonVisibility.Hidden, SelectionCommand.RecognizeQr, false),
            new(ToolbarActionId.CopyCodeText, "CaptureCodeCopyText", "RecognizeBarcode",
                ToolbarButtonVisibility.Hidden, SelectionCommand.CopyCodeText, false),
            new(ToolbarActionId.OutputColor, "OutputColorMode", "OutputColorMode", ToolbarButtonVisibility.Hidden,
                Configurable: false),
            new(ToolbarActionId.ExportPdf, "DocumentExport", "DocumentExport", ToolbarButtonVisibility.Hidden,
                Configurable: false),
        ];

        internal static IReadOnlyList<ToolbarActionDefinition> Configurable { get; } =
            [.. All.Where(definition => definition.Configurable)];

        public static ToolbarActionDefinition Get(ToolbarActionId id)
        {
            return All.First(definition => definition.Id == id);
        }
    }
}
