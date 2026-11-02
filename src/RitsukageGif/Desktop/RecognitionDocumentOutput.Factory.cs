namespace RitsukageGif.Desktop
{
    internal sealed partial class RecognitionDocumentOutput
    {
        internal static RecognitionDocumentOutput Shared { get; } = new(new FormulaDocxService(),
            new TableOutputService(),
            new(new(AtomicFileOutput.WriteBytesAsync), new SystemDocumentLauncher()),
            AtomicFileOutput.WriteBytesAsync,
            (owner, table, options, title, token) => ScreenshotPrintProcess.PrintTableAsync(owner, table, options,
                title, new TablePrintRenderer(), null, token),
            () => ScreenshotPrintService.IsAvailable,
            ScreenshotPrintService.GetCapabilitiesAsync);
    }
}
