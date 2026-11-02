namespace RitsukageGif.Desktop
{
    internal static partial class GlobalActions
    {
        private static readonly Dictionary<GlobalActionKind, GlobalActionDefinition> Definitions;

        static GlobalActions()
        {
            Definitions = Catalog.ToDictionary(definition => definition.Kind);
        }

        internal static IReadOnlyList<GlobalActionDefinition> Catalog { get; } =
            Array.AsReadOnly<GlobalActionDefinition>([
                new(GlobalActionKind.SelectRegion, "ScreenshotSelectAnnotation"),
                new(GlobalActionKind.ToggleRecording, "ScreenshotRecord"),
                new(GlobalActionKind.StartRecording, "ScreenshotRecord"),
                new(GlobalActionKind.StopRecording, "ScreenshotRecord"),
                new(GlobalActionKind.Screenshot, "ScreenshotHeading", ActionScreenshotOutput.Copy, AutoConfirm: false),
                new(GlobalActionKind.RunPreset, "GlobalActionsHeading", FileParameter: true),
                new(GlobalActionKind.OpenScriptEditor, "GlobalActionsHeading", FileParameter: true),
                new(GlobalActionKind.OpenImageEditor, "AnimationBrush", FileParameter: true),
                new(GlobalActionKind.OpenVideoRecorder, "ScreenshotRecord"),
                new(GlobalActionKind.OpenOutputDirectory, "ScreenshotSaveHeading"),
                new(GlobalActionKind.ShowMainWindow, "GeneralHeading"),
                new(GlobalActionKind.HideMainWindow, "GeneralHeading"),
                new(GlobalActionKind.ShowPinnedImages, "ScreenshotPin"),
                new(GlobalActionKind.HidePinnedImages, "ScreenshotPin"),
                new(GlobalActionKind.ScreenshotAndCopy, "ScreenshotCopy", ActionScreenshotOutput.Copy),
                new(GlobalActionKind.ScreenshotAndPin, "ScreenshotPin", ActionScreenshotOutput.Pin),
                new(GlobalActionKind.ScreenshotAndSave, "ScreenshotSave", ActionScreenshotOutput.Save),
                new(GlobalActionKind.ScreenshotQuickSave, "ScrollCaptureQuickSave", ActionScreenshotOutput.QuickSave),
                new(GlobalActionKind.CaptureActiveWindow, "ScreenshotHeading", ActionScreenshotOutput.Copy, true),
                new(GlobalActionKind.CaptureLastRegion, "ScreenshotHeading", ActionScreenshotOutput.Copy, true),
                new(GlobalActionKind.RestoreLastPinnedImage, "ScreenshotPin"),
                new(GlobalActionKind.PinClipboard, "ScreenshotPin"),
                new(GlobalActionKind.ScreenshotAndCopyText, "RecognizeText", ActionScreenshotOutput.CopyText),
                new(GlobalActionKind.ScreenshotAndCopyCodeText, "RecognizeBarcode",
                    ActionScreenshotOutput.CopyCodeText),
                new(GlobalActionKind.ScreenshotAndScroll, "ScreenshotScroll", ActionScreenshotOutput.Copy,
                    StartWithScrolling: true),
                new(GlobalActionKind.ClosePinnedImages, "Cancel"),
                new(GlobalActionKind.TogglePinnedImages, "ScreenshotPin"),
                new(GlobalActionKind.ToggleOtherPinnedImages, "ScreenshotPin"),
                new(GlobalActionKind.SwitchPinnedGroup, "SettingsPinsHeading"),
                new(GlobalActionKind.ToggleGlobalShortcuts, "SettingsLocalInput"),
                new(GlobalActionKind.ScreenshotAndTranslate, "TranslationRun", ActionScreenshotOutput.Translate),
                new(GlobalActionKind.TranslateClipboard, "TranslationRun"),
                new(GlobalActionKind.ScreenshotAndRecognizeText, "RecognizeText", ActionScreenshotOutput.RecognizeText),
                new(GlobalActionKind.ScreenshotAndRecognizeTable, "TableRecognition",
                    ActionScreenshotOutput.RecognizeTable),
                new(GlobalActionKind.ScreenshotAndRecognizeFormula, "FormulaRecognition",
                    ActionScreenshotOutput.RecognizeFormula),
                new(GlobalActionKind.OpenAnimationEditor, "AnimationPreview", FileParameter: true),
                new(GlobalActionKind.RunScript, "GlobalActionsHeading"),
            ]);

        internal static GlobalActionDefinition Definition(GlobalActionKind kind)
        {
            return Definitions[kind];
        }
    }
}
