using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed class ClipboardFileReference(string path, IStorageProvider storage) : IStorageItem
    {
        public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path.LocalPath))
            is { Length: > 0 } name
            ? name
            : Path.LocalPath;

        public Uri Path { get; private set; } = new(System.IO.Path.GetFullPath(path));
        public bool CanBookmark => false;

        public void Dispose()
        {
        }

        public async Task<StorageItemProperties> GetBasicPropertiesAsync()
        {
            using var item = await ResolveAsync();
            return item is not null ? await item.GetBasicPropertiesAsync() : new();
        }

        public Task<string?> SaveBookmarkAsync()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IStorageFolder?> GetParentAsync()
        {
            var parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(Path.LocalPath));
            return parent is not null
                ? storage.TryGetFolderFromPathAsync(new(parent))
                : Task.FromResult<IStorageFolder?>(null);
        }

        public async Task DeleteAsync()
        {
            using var item = await ResolveAsync() ?? throw new FileNotFoundException(null, Path.LocalPath);
            await item.DeleteAsync();
        }

        public async Task<IStorageItem?> MoveAsync(IStorageFolder destination)
        {
            var item = await ResolveAsync() ?? throw new FileNotFoundException(null, Path.LocalPath);
            IStorageItem? moved = null;
            try
            {
                moved = await item.MoveAsync(destination);
                if (moved is not null) Path = moved.Path;
                return moved;
            }
            finally
            {
                if (!ReferenceEquals(item, moved)) item.Dispose();
            }
        }

        private async Task<IStorageItem?> ResolveAsync()
        {
            return (IStorageItem?)await storage.TryGetFileFromPathAsync(Path) ??
                   await storage.TryGetFolderFromPathAsync(Path);
        }
    }
}
