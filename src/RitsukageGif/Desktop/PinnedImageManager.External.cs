using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public async Task<bool> PinFilesAsync(IReadOnlyList<string> paths, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(paths);
            token.ThrowIfCancellationRequested();
            var kinds = Preferences.SupportedContentKinds;
            if (kinds == PinnedContentKinds.None || paths.Count == 0) return false;
            var files = paths.Select(Path.GetFullPath).ToArray();
            if ((kinds & PinnedContentKinds.Images) != 0 &&
                await TryPrepareFileImageAsync(files, token) is { } image)
            {
                Pin(image, null, "");
                return true;
            }

            var snapshot = new PinnedClipboardSnapshot([], null, files, null, null);
            if (PinnedContentRegistry.Create(snapshot, kinds) is not { } document) return false;
            PinDocument(document);
            return true;
        }

        private Task<PinnedImageContent?> TryPrepareFileImageAsync(string[] paths,
            CancellationToken token)
        {
            if (paths.Length != 1 || !File.Exists(paths[0]) ||
                Path.GetExtension(paths[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<PinnedImageContent?>(null);
            var path = paths[0];
            return TryPrepareClipboardAsync(async () =>
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes) return null;
                var encoded = await File.ReadAllBytesAsync(path, token);
                return await Task.Run(() => PinnedImageContent.Decode(encoded, token), token);
            }, token);
        }
    }
}
