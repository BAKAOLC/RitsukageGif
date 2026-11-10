using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController
    {
        private async Task<string?> PrepareQuickRecordingAsync(Window owner, CancellationToken token)
        {
            _preferences = _host.Settings.ScreenshotRecording;
            if (!_preferences.SkipQuickReminder)
            {
                var dialog = new QuickRecordingDialog(_preferences.QuickQuality);
                await using var registration = token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                var confirmed = await dialog.ShowDialog<bool>(owner);
                token.ThrowIfCancellationRequested();
                if (dialog.SkipReminder)
                {
                    _preferences = _host.Settings.ScreenshotRecording with { SkipQuickReminder = true };
                    _host.UpdateScreenshotRecordingPreferences(_preferences);
                }

                if (!confirmed) return null;
                _preferences = _host.Settings.ScreenshotRecording with { QuickQuality = dialog.Quality };
                _host.UpdateScreenshotRecordingPreferences(_preferences);
            }

            var directory = Path.GetDirectoryName(_preferences.QuickLastPath);
            using var folder = string.IsNullOrEmpty(directory)
                ? null
                : await StoragePickerAwaiter.WaitAsync(owner.StorageProvider.TryGetFolderFromPathAsync(directory),
                    token);
            using var file = await StoragePickerAwaiter.WaitAsync(owner.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = UiText.Get("RecordingSaveRecording"), DefaultExtension = "mp4",
                SuggestedFileName = "Recording-" +
                                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".mp4",
                SuggestedStartLocation = folder, ShowOverwritePrompt = true,
                FileTypeChoices = [new("MP4") { Patterns = ["*.mp4"] }],
            }), token);
            if (file is null) return null;
            var path = file.TryGetLocalPath() ?? throw new NotSupportedException(UiText.Get("FileOperationFailed"));
            _preferences = _host.Settings.ScreenshotRecording with { QuickLastPath = path };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
            return path;
        }
    }
}
