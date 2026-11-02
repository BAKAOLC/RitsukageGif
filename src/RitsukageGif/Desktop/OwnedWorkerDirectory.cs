namespace RitsukageGif.Desktop
{
    internal sealed class OwnedWorkerDirectory : IDisposable
    {
        private const string OwnerFile = "owner.lock";
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.WorkerStorage");
        private readonly FileStream _owner;
        private bool _disposed;

        private OwnedWorkerDirectory(string path, FileStream owner)
        {
            DirectoryPath = path;
            _owner = owner;
        }

        internal string DirectoryPath { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                Remove(DirectoryPath, _owner);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.ZLogWarning(error, $"Worker temporary storage cleanup was deferred.");
            }
        }

        internal static OwnedWorkerDirectory Create(string kind, string? rootDirectory = null)
        {
            var root = Path.Combine(rootDirectory ?? Path.Combine(Path.GetTempPath(), "RitsukageGif", "Workers"), kind);
            Directory.CreateDirectory(root);
            ReclaimOrphans(root);
            var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
            else
                Directory.CreateDirectory(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                return new(path, new(Path.Combine(path, OwnerFile), FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None));
            }
            catch
            {
                Directory.Delete(path);
                throw;
            }
        }

        private static void ReclaimOrphans(string root)
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                FileStream owner;
                try
                {
                    owner = new(Path.Combine(directory, OwnerFile), FileMode.Open, FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException error)
                {
                    Logger.ZLogWarning(error, $"Worker storage ownership could not be checked.");
                    continue;
                }

                try
                {
                    Remove(directory, owner);
                    Logger.ZLogDebug($"An abandoned worker directory was reclaimed.");
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Abandoned worker data cleanup was deferred.");
                }
            }
        }

        private static void Remove(string directory, FileStream owner)
        {
            using (owner)
            {
                // Keep the ownership marker until all worker data can be removed.
                foreach (var file in Directory.EnumerateFiles(directory))
                    if (Path.GetFileName(file) != OwnerFile)
                        File.Delete(file);
                foreach (var child in Directory.EnumerateDirectories(directory)) Directory.Delete(child, true);
            }

            File.Delete(Path.Combine(directory, OwnerFile));
            Directory.Delete(directory);
        }
    }
}
