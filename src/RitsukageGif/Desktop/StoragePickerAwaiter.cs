using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal static class StoragePickerAwaiter
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.StoragePicker");

        internal static Task<IStorageFile?> WaitAsync(Task<IStorageFile?> picker, CancellationToken token)
        {
            return WaitAsync(picker, static file => file?.Dispose(), token);
        }

        internal static Task<IStorageFolder?> WaitAsync(Task<IStorageFolder?> picker, CancellationToken token)
        {
            return WaitAsync(picker, static folder => folder?.Dispose(), token);
        }

        internal static Task<IReadOnlyList<IStorageFile>> WaitAsync(
            Task<IReadOnlyList<IStorageFile>> picker, CancellationToken token)
        {
            return WaitAsync(picker, DisposeFiles, token);
        }

        internal static Task<IReadOnlyList<IStorageFolder>> WaitAsync(
            Task<IReadOnlyList<IStorageFolder>> picker, CancellationToken token)
        {
            return WaitAsync(picker, DisposeFolders, token);
        }

        internal static void DisposeFolders(IReadOnlyList<IStorageFolder> folders)
        {
            foreach (var folder in folders) Release(folder, static item => item.Dispose());
        }

        internal static void DisposeFiles(IReadOnlyList<IStorageFile> files)
        {
            foreach (var file in files) Release(file, static item => item.Dispose());
        }

        private static async Task<T> WaitAsync<T>(Task<T> picker, Action<T> release, CancellationToken token)
        {
            T result;
            try
            {
                result = await picker.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _ = ObserveLateAsync(picker, release);
                throw;
            }

            if (!token.IsCancellationRequested) return result;
            Release(result, release);
            token.ThrowIfCancellationRequested();
            return result;
        }

        private static async Task ObserveLateAsync<T>(Task<T> picker, Action<T> release)
        {
            try
            {
                Release(await picker, release);
            }
            catch (OperationCanceledException)
            {
                Logger.ZLogDebug($"Storage picker canceled after its owner closed.");
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Storage picker failed after its owner closed.");
            }
        }

        private static void Release<T>(T result, Action<T> release)
        {
            try
            {
                release(result);
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Storage picker result disposal failed.");
            }
        }
    }
}
