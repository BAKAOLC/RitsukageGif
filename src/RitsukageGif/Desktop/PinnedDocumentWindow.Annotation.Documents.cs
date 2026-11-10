namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        internal Action<IReadOnlyList<string>>? OutputNotification { get; init; }
        internal Action<IReadOnlyList<string>, IReadOnlyList<string>?>? DocumentOutputSaved { get; init; }

        internal Func<byte[], CapturedFrame?, ScreenshotAutoSaveTriggers, CancellationToken, double, Task>?
            AutomaticSave { get; init; }

        private Task SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers trigger, ScreenshotResult output)
        {
            return AutomaticSave?.Invoke(output.Png, output.Hdr, trigger, _lifetime.Token, output.CaptureScaling) ??
                   Task.CompletedTask;
        }

        private async Task QuickSaveRenderedImageAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            var profile = SavePreferencesProvider().Normalize().Quick;
            var dimensions = PngImageCodec.ReadDimensions(output.Png);
            var size = profile.OutputSize.Resolve(dimensions.Width, dimensions.Height);
            var context = new ScreenshotNameContext(DateTime.Now, Width: size.Width, Height: size.Height);
            var path = Path.ChangeExtension(profile.CreatePath(OutputDirectoryProvider() ??
                                                               Environment.GetFolderPath(Environment.SpecialFolder
                                                                   .MyPictures), context),
                profile.DialogExtension(output.ColorMode));
            var saved = await ScreenshotImageStorage.SaveAsync(output, path, profile, token);
            SavedPathChanged?.Invoke(saved.Paths[0]);
            await SaveDocumentAutomaticallyAsync(ScreenshotAutoSaveTriggers.Save, output);
            await CompleteSelectionSaveAsync(saved.Paths, profile, output);
        }

        private async Task ExportDocumentImageAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var dimensions = PngImageCodec.ReadDimensions(output.Png);
            var context = new ScreenshotNameContext(DateTime.Now, Width: dimensions.Width, Height: dimensions.Height);
            var path = await DocumentExportWindow.ShowAsync(this, output.Png,
                Path.ChangeExtension(profile.CreateFileName(context), ".pdf"), token,
                profile.ResolveDirectory(
                    OutputDirectoryProvider() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    context));
            if (path is null) return;
            try
            {
                await CompleteSelectionSaveAsync([path], profile, output);
            }
            finally
            {
                DocumentOutputSaved?.Invoke([path], profile.AfterSave is ScreenshotAfterSaveAction.CopyFile or
                    ScreenshotAfterSaveAction.CopyFileAndPath
                    ? [path]
                    : null);
            }
        }

        private async Task PrintDocumentImageAsync()
        {
            var token = _lifetime.Token;
            var output = await CapturePreferredOutputAsync(OutputColorMode.Sdr, token);
            await ScreenshotPrintService.PrintAsync(TryGetPlatformHandle()?.Handle ?? 0, output.Png, "RitsukageGif",
                token);
        }
    }
}
