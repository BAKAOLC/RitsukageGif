using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingReviewWindow
    {
        private async Task SaveAsync(bool quick)
        {
            if (_source is null) return;
            await _mixing;
            await SetPlayingAsync(false);
            var token = _lifetime.Token;
            var preferences = _document.Output.Preferences().Normalize();
            var profile = quick ? preferences.Quick : preferences.ManualProfile();
            var context = OutputNameContext();
            var name = CreateOutputName(profile, context);
            var extension = Path.GetExtension(name);
            string? destination;
            if (quick)
            {
                destination = QuickSaveDestination(profile, name, context);
            }
            else
            {
                using var folder = await StoragePickerAwaiter.WaitAsync(
                    StorageProvider.TryGetFolderFromPathAsync(
                        profile.ResolveDirectory(_document.Output.FallbackDirectory(), context)), token);
                using var file = await StoragePickerAwaiter.WaitAsync(StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = UiText.Get("RecordingReviewSave"), SuggestedFileName = name,
                    DefaultExtension = extension.TrimStart('.'), SuggestedStartLocation = folder,
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                        [new(extension.TrimStart('.').ToUpperInvariant()) { Patterns = ["*" + extension] }],
                }), token);
                if (file is null) return;
                destination = file.TryGetLocalPath() ??
                              throw new NotSupportedException(UiText.Get("FileOperationFailed"));
            }

            var outputs = RecordingReviewExport.GetOutputPaths(_document, destination, _outputFormat);
            if (!quick && !File.Exists(destination) && outputs.Skip(1).Any(File.Exists))
                throw new IOException(UiText.Get("HdrOutputExists"));
            var saved = await ExportAsync(destination,
                !quick || profile.ConflictPolicy == ScreenshotConflictPolicy.Overwrite);
            if (saved.Count == 0) return;
            _exported = true;
            try
            {
                await ScreenshotSaveCompletion.RunAsync(saved, profile, Clipboard, StorageProvider,
                    () => _document.Output.CopyFiles(saved), _document.Output.Notify, CancellationToken.None);
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Recording was saved, but its completion action failed.");
                if (!_closing) _status.Text = error.Message;
            }
            finally
            {
                if (_exitAfterExport) Close();
            }
        }

        private ScreenshotNameContext OutputNameContext()
        {
            return new(DateTime.Now, _document.CaptureTitle, _document.CaptureProcess,
                _source!.OriginalWidth, _source.OriginalHeight);
        }

        private string CreateOutputName(ScreenshotSaveProfile profile, ScreenshotNameContext context)
        {
            var name = Path.ChangeExtension(profile.CreateFileName(context), _outputFormat);
            return _document.Colors == OutputColorMode.Hdr && _outputFormat != "mp4"
                ? Path.GetFileNameWithoutExtension(name) + ".hdr.png"
                : name;
        }

        private string QuickSaveDestination(ScreenshotSaveProfile profile, string name, ScreenshotNameContext context)
        {
            var directory = profile.ResolveDirectory(_document.Output.FallbackDirectory(), context);
            var path = Path.Combine(directory, name);
            var destination = path;
            for (var index = 1;
                 RecordingReviewExport.GetOutputPaths(_document, destination, _outputFormat).Any(Path.Exists);
                 index++)
            {
                switch (profile.ConflictPolicy)
                {
                    case ScreenshotConflictPolicy.Overwrite: return destination;
                    case ScreenshotConflictPolicy.Fail: throw new IOException(UiText.Get("HdrOutputExists"));
                    case ScreenshotConflictPolicy.Rename: break;
                    default: throw new ArgumentOutOfRangeException(nameof(profile));
                }

                destination = Path.Combine(directory,
                    Path.GetFileNameWithoutExtension(path) + " (" + index + ")" + Path.GetExtension(path));
            }

            return destination;
        }

        private async Task CopyAsync()
        {
            if (_source is null) return;
            await _mixing;
            await SetPlayingAsync(false);
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var profile = _document.Output.Preferences().Normalize().ManualProfile();
            var name = CreateOutputName(profile, OutputNameContext());
            var files = new RecordingClipboardFileStore();
            if (!await files.PublishAsync(clipboard, StorageProvider, name,
                    (path, _) => ExportAsync(path, false), _lifetime.Token)) return;
            _exported = true;
            if (_exitAfterExport) Close();
        }

        private async Task<IReadOnlyList<string>> ExportAsync(string destination, bool overwrite)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _export = cancellation;
            SetActionsEnabled(false);
            _timeline.IsEnabled = false;
            try
            {
                var options = new RecordingReviewExportOptions(_outputFormat, _quality, _timeline.RangeStart,
                    _timeline.RangeEnd, _rate)
                {
                    InputEffects = _inputEffects, ProgressEffects = _progressEffects,
                    AudioMix = _audio?.Mix.ToArray() ?? [],
                    Overwrite = overwrite,
                };
                _exitAfterExport = false;
                _toolbar.IsOpen = false;
                var result = await RecordingReviewExportWindow.ShowAsync(this, _autoExitAfterEncoding,
                    (progress, token) =>
                        RecordingReviewExport.WriteAsync(_document, destination, options, token, progress),
                    value =>
                    {
                        _autoExitAfterEncoding = value;
                        _document.AutoExitAfterEncodingChanged?.Invoke(value);
                    }, cancellation.Token);
                if (result is null) return [];
                _exitAfterExport = result.ExitRecording;
                _autoExitAfterEncoding = result.AutoExitAfterEncoding;
                return result.Outputs;
            }
            finally
            {
                _export = null;
                if (!_closing)
                {
                    _toolbar.IsOpen = true;
                    SetActionsEnabled(true);
                    _timeline.IsEnabled = true;
                    BuildAudioControls();
                    RefreshInputCapabilities();
                }
            }
        }
    }
}
