using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class PinnedDocumentStorage
    {
        public static async Task<string> SaveAsync(string path, string content, ScreenshotConflictPolicy policy,
            CancellationToken token)
        {
            path = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!,
                ".document-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                token.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token);
                for (;;)
                {
                    token.ThrowIfCancellationRequested();
                    var destination = new ScreenshotSaveProfile { ConflictPolicy = policy }.ResolvePath(path);
                    try
                    {
                        File.Move(temporary, destination, policy == ScreenshotConflictPolicy.Overwrite);
                        return destination;
                    }
                    catch (IOException) when (policy == ScreenshotConflictPolicy.Rename && Path.Exists(destination))
                    {
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
