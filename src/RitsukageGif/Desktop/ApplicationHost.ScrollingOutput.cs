namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<bool> SaveStreamingScreenshotAsync(ScreenshotScrollingWindow owner,
            ScrollingCaptureAction action, OutputColorMode mode, double captureScaling, CaptureDecoration decoration,
            ScreenshotAnnotationSession annotations, ScreenshotInvocation? invocation, CancellationToken token)
        {
            var quick = action == ScrollingCaptureAction.QuickSave;
            var profile = quick ? ScreenshotSaveForOutput.Normalize().Quick : ScreenshotSaveForOutput.ManualProfile();
            var source = owner.StreamingSize;
            var geometry = CaptureDecorationProcessor.MeasureStreaming(source.Width, source.Height, decoration);
            var scale = Settings.DpiOutput.ScaleFor(DpiOutputFeature.LongScreenshotSave, captureScaling);
            var size = StreamingOutputSize(profile.OutputSize, geometry.Width, geometry.Height, scale);
            var target = _screenshotSelection?.Snapshot.Target;
            var name = new ScreenshotNameContext(DateTime.Now, target?.Title, target?.Process, size.Width, size.Height);
            var path = quick
                ? profile.CreatePath(Settings.OutputDirectory, name, StaticImageFormat.Png)
                : await ScreenshotOutputStorage.PickPathAsync(owner, owner.StorageProvider, profile,
                    Settings.OutputDirectory, "Screenshot", mode, Settings.Appearance.SaveDialogStyle, name,
                    true, token);
            if (path is null) return false;
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(UiText.Get("ScrollCaptureStreamingPngRequired"));
            var output = await ScreenshotScrollingSaveProgressWindow.RunAsync(owner, async cancellation =>
            {
                try
                {
                    await owner.PrepareStreamingAnnotationsAsync(annotations, mode, cancellation);
                    return await Task.Run(() => WriteStreamingScreenshots(owner, path, profile, mode, size,
                        decoration, cancellation), cancellation);
                }
                finally
                {
                    owner.ClearStreamingAnnotations();
                }
            }, token);
            if (output is null) return false;
            LastScreenshotOutput = output;
            invocation?.AddPaths(output.Paths);
            SetStatus(UiText.Format("AnimationExported", string.Join(", ", output.Paths)));
            PlayScreenshotSound(quick ? ScreenshotSoundTiming.QuickSave : ScreenshotSoundTiming.Save);
            try
            {
                if (!quick) PersistScreenshotExtension(path);
                var completionProfile = profile.AfterSave == ScreenshotAfterSaveAction.CopyImage
                    ? profile with { AfterSave = ScreenshotAfterSaveAction.CopyFile }
                    : profile;
                await ScreenshotSaveCompletion.RunAsync(output.Paths, completionProfile, _services.Context.Clipboard,
                    _services.Context.StorageProvider, () => Task.CompletedTask, ShowOutputNotification, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                SetStatus(error.Message);
                HostLogger.ZLogWarning(error, $"Post-save operation for streamed scrolling capture failed.");
            }

            return true;
        }

        private static (int Width, int Height) StreamingOutputSize(ScreenshotOutputSize size, int width, int height,
            double scale)
        {
            var (outputWidth, outputHeight) = size.Measure(width, height);
            return (Dimension(outputWidth), Dimension(outputHeight));

            int Dimension(double value)
            {
                return checked((int)Math.Max(1, Math.Round(value * scale, MidpointRounding.AwayFromZero)));
            }
        }

        private static ScreenshotSavedOutput WriteStreamingScreenshots(ScreenshotScrollingWindow owner, string path,
            ScreenshotSaveProfile profile, OutputColorMode mode, (int Width, int Height) size,
            CaptureDecoration decoration, CancellationToken token)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            var stem = Path.GetFileNameWithoutExtension(fullPath);
            var hdr = mode == OutputColorMode.Both ? new[] { false, true } : [mode == OutputColorMode.Hdr];
            var stages = hdr.Select(_ => Path.Combine(directory, $".{Guid.NewGuid():N}.stage")).ToArray();
            try
            {
                for (var index = 0; index < stages.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = new FileStream(stages[index], FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 64 * 1024, FileOptions.SequentialScan);
                    owner.WriteStreamingPng(stream, size.Width, size.Height, hdr[index], decoration, token);
                }

                for (var index = 0;; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var suffix = index == 0 ? "" : $" ({index})";
                    string[] destinations = mode == OutputColorMode.Both
                        ?
                        [
                            Path.Combine(directory, stem + suffix + ".sdr.png"),
                            Path.Combine(directory, stem + suffix + ".hdr.png"),
                        ]
                        : [Path.Combine(directory, stem + suffix + ".png")];
                    var rename = profile.ConflictPolicy == ScreenshotConflictPolicy.Rename;
                    if (rename && destinations.Any(Path.Exists)) continue;
                    AtomicFileOutput.PublishStaged(stages, destinations,
                        profile.ConflictPolicy == ScreenshotConflictPolicy.Overwrite);
                    return new(mode, destinations);
                }
            }
            finally
            {
                foreach (var stage in stages) File.Delete(stage);
            }
        }
    }
}
