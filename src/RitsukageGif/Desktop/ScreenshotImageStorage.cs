namespace RitsukageGif.Desktop
{
    internal static class ScreenshotImageStorage
    {
        internal static async Task<ScreenshotSavedOutput> SaveAsync(ScreenshotResult result,
            string path, ScreenshotSaveProfile profile, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            result = await ScreenshotOutputTransform.ResizeAsync(result, profile.OutputSize, token);
            if (result.ColorMode != OutputColorMode.Sdr && result.Hdr is not { ColorInfo.IsHdrSource: true })
                throw new NotSupportedException(result.HdrUnavailableReason ?? UiText.Get("HdrCaptureUnavailable"));
            switch (result.ColorMode)
            {
                case OutputColorMode.Sdr:
                    var image = await ScreenshotOutputStorage.SaveAsync(result.Png, path, profile, token);
                    return new(result.ColorMode, [image.Path]);
                case OutputColorMode.Hdr:
                    for (;;)
                    {
                        token.ThrowIfCancellationRequested();
                        var target = profile.ResolvePath(path);
                        try
                        {
                            var hdr = await HdrImageService.SaveAsync(result.Hdr!, target,
                                new() { Overwrite = profile.ConflictPolicy == ScreenshotConflictPolicy.Overwrite },
                                token);
                            return new(result.ColorMode, [hdr.Path]);
                        }
                        catch (IOException) when (profile.ConflictPolicy == ScreenshotConflictPolicy.Rename &&
                                                  Path.Exists(target))
                        {
                        }
                    }
                case OutputColorMode.Both:
                    var pair = await SavePairedScreenshotAsync(result, path, profile, token);
                    return new(result.ColorMode, [pair.Sdr.Path, pair.Hdr.Path]);
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        }

        private static async Task<(MediaInfo Sdr, MediaInfo Hdr)> SavePairedScreenshotAsync(ScreenshotResult result,
            string path, ScreenshotSaveProfile preferences, CancellationToken token)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var stem = Path.GetFileNameWithoutExtension(path);
            for (var index = 0;; index++)
            {
                token.ThrowIfCancellationRequested();
                var suffix = index == 0 ? "" : $" ({index})";
                var sdrPath = Path.Combine(directory, stem + suffix + ".sdr.png");
                var hdrPath = Path.Combine(directory, stem + suffix + ".hdr.png");
                if (preferences.ConflictPolicy == ScreenshotConflictPolicy.Rename &&
                    (Path.Exists(sdrPath) || Path.Exists(hdrPath))) continue;
                try
                {
                    return await PairedImageService.SaveAsync(result.Png, result.Hdr!, sdrPath, hdrPath,
                        preferences.ConflictPolicy == ScreenshotConflictPolicy.Overwrite, token);
                }
                catch (IOException) when (preferences.ConflictPolicy == ScreenshotConflictPolicy.Rename &&
                                          (Path.Exists(sdrPath) || Path.Exists(hdrPath)))
                {
                }
            }
        }
    }
}
