namespace RitsukageGif.Scripting
{
    public static partial class FileScriptLibrary
    {
        private static async Task WriteAtomically(string destination, bool overwrite,
            Func<Stream, CancellationToken, Task> write, CancellationToken token)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetRandomFileName());
            var created = false;
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    created = true;
                    await write(stream, token);
                    await stream.FlushAsync(token);
                }

                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite);
            }
            finally
            {
                if (created)
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
            }
        }
    }
}
