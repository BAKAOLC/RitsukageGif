using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotSaveCompletion
    {
        internal static async Task RunAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            IClipboard? clipboard, IStorageProvider storage, Func<Task> copyImage,
            Action<IReadOnlyList<string>>? notify, CancellationToken token = default)
        {
            if (paths.Count == 0 || paths.Any(path => !File.Exists(path)))
                throw new IOException(UiText.Get("NoInputFrames"));
            token.ThrowIfCancellationRequested();
            switch (profile.AfterSave)
            {
                case ScreenshotAfterSaveAction.None: break;
                case ScreenshotAfterSaveAction.CopyImage:
                    await copyImage();
                    break;
                case ScreenshotAfterSaveAction.CopyFile:
                    if (clipboard is null) throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    var transfer = new DataTransfer();
                    foreach (var path in paths)
                    {
                        var file = await storage.TryGetFileFromPathAsync(path) ??
                                   throw new IOException(UiText.Get("ClipboardFailed"));
                        transfer.Add(DataTransferItem.CreateFile(file));
                    }

                    token.ThrowIfCancellationRequested();
                    await clipboard.SetDataAsync(transfer);
                    await clipboard.FlushAsync();
                    break;
                case ScreenshotAfterSaveAction.CopyPath:
                    if (clipboard is null) throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    await clipboard.SetTextAsync(string.Join(Environment.NewLine, paths));
                    await clipboard.FlushAsync();
                    break;
                case ScreenshotAfterSaveAction.OpenFolder:
                    PlatformShell.RevealFile(paths[0]);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(profile));
            }

            if (profile.Notify) notify?.Invoke(paths);
        }
    }
}
