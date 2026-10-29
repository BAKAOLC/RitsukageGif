namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Func<ScreenshotSavePreferences> SavePreferencesProvider { get; set; } = () => new();
        public Func<ScreenshotClipboardPreferences> ClipboardPreferencesProvider { get; set; } = () => new();
        public Func<string?> OutputDirectoryProvider { get; set; } = () => null;
        public Func<AppearanceDialogStyle> DialogStyleProvider { get; set; } = () => AppearanceDialogStyle.Modern;
        public Action<string>? SavedPathChanged { get; set; }
        public Action<IReadOnlyList<string>>? OutputNotification { get; set; }

        public Func<byte[], CapturedFrame?, ScreenshotAutoSaveTriggers, CancellationToken, double, Task>? AutomaticSave
        {
            get;
            set;
        }

        private async Task CopyFileAsync()
        {
            var output = await CaptureOutputAsync(OutputColorModeProvider(), _lifetime.Token);
            await CopyImageAsync(output, true);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
        }

        private async Task CopyImageAsync(ScreenshotResult output, bool forceFile = false)
        {
            PinLogger.ZLogDebug($"Pinned image clipboard operation started.");
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var preferences = ClipboardPreferencesProvider().Normalize();
            if (forceFile) preferences = preferences with { Mode = ScreenshotClipboardMode.File };
            var quality = SavePreferencesProvider().Normalize().Quality;
            await ClipboardImage.CopyAsync(clipboard, output, preferences, StorageProvider, quality, _lifetime.Token);
            PinLogger.ZLogInformation($"Pinned image clipboard operation completed.");
        }

        private async Task SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers trigger, ScreenshotResult output)
        {
            if (AutomaticSave is null ||
                (SavePreferencesProvider().Normalize().AutoSaveTriggers & trigger) == 0) return;
            var token = _lifetime.Token;
            PinLogger.ZLogDebug($"Pinned image automatic save started.");
            await AutomaticSave(output.Png, output.Hdr, trigger, token, output.CaptureScaling);
            PinLogger.ZLogInformation($"Pinned image automatic save completed.");
        }

        private async Task SaveOutputAsync(ScreenshotResult result)
        {
            PinLogger.ZLogDebug($"Pinned image save dialog requested.");
            var profile = SavePreferencesProvider().Normalize().ManualProfile();
            var token = _lifetime.Token;
            var dimensions = PngImageCodec.ReadDimensions(result.Png);
            var size = profile.OutputSize.Resolve(dimensions.Width, dimensions.Height);
            var path = await ScreenshotOutputStorage.PickPathAsync(this, StorageProvider, profile,
                OutputDirectoryProvider(), "PinnedImage", result.ColorMode, DialogStyleProvider(),
                new(DateTime.Now, Width: size.Width, Height: size.Height), token);
            if (path is null)
            {
                PinLogger.ZLogDebug($"Pinned image save dialog cancelled.");
                return;
            }

            var saved = await ScreenshotImageStorage.SaveAsync(result, path, profile, token);
            SavedPathChanged?.Invoke(saved.Paths[0]);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Save, result);
            await ScreenshotSaveCompletion.RunAsync(saved.Paths, profile, Clipboard, StorageProvider,
                () => CopyImageAsync(result), OutputNotification, token);
            PinLogger.ZLogInformation($"Pinned image save completed. FileCount={saved.Paths.Count}");
        }
    }
}
