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
            var name = Path.ChangeExtension(profile.CreateFileName(new ScreenshotNameContext(DateTime.Now,
                Width: dimensions.Width, Height: dimensions.Height)), ".pdf");
            var path = await DocumentExportWindow.ShowAsync(this, output.Png, name, token,
                profile.ResolveDirectory(OutputDirectoryProvider() ??
                                         Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));
            if (path is null) return;
            try
            {
                await CompleteSelectionSaveAsync([path], profile, output);
            }
            finally
            {
                DocumentOutputSaved?.Invoke([path],
                    profile.AfterSave == ScreenshotAfterSaveAction.CopyFile ? [path] : null);
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
