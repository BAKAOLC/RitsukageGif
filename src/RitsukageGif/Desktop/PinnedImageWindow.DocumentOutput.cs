namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Action<IReadOnlyList<string>, IReadOnlyList<string>?>? DocumentOutputSaved { get; set; }

        private static IReadOnlyList<SelectionCommandDefinition> AnnotationOutputShortcutBindings()
        {
            return
            [
                .. SelectionCommands.SnapshotBindings().Where(binding => binding.Command != SelectionCommand.Print),
                .. BuiltinShortcuts.Snapshot()
                    .Where(binding => binding is { ContextKey: "BuiltinShortcutPinnedContext", Action: "print" })
                    .Select(binding =>
                        new SelectionCommandDefinition(SelectionCommand.Print,
                            binding.Gesture.Key, binding.Gesture.KeyModifiers, "Print")),
            ];
        }

        private async Task ExportDocumentAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var dimensions = PngImageCodec.ReadDimensions(output.Png);
            var context = new ScreenshotNameContext(DateTime.Now,
                Width: dimensions.Width, Height: dimensions.Height);
            var name = Path.ChangeExtension(profile.CreateFileName(context), ".pdf");
            var path = await DocumentExportWindow.ShowAsync(this, output.Png, name, token,
                profile.ResolveDirectory(OutputDirectoryProvider() ??
                                         Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), context));
            if (path is null) return;
            try
            {
                await CompleteSelectionSaveAsync([path], profile, output);
            }
            finally
            {
                DocumentOutputSaved?.Invoke([path],
                    profile.AfterSave is ScreenshotAfterSaveAction.CopyFile or
                        ScreenshotAfterSaveAction.CopyFileAndPath
                        ? [path]
                        : null);
            }
        }

        private async Task PrintDocumentAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            await ScreenshotPrintService.PrintAsync(TryGetPlatformHandle()?.Handle ?? 0, output.Png,
                "RitsukageGif", token);
        }
    }
}
