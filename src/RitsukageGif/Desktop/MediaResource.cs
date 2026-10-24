using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class MediaResource(string path, bool temporary) : IDisposable
    {
        private static readonly HttpClient Client = new();
        private bool _disposed;
        public string Path { get; } = path;

        public static async Task<MediaResource> OpenAsync(string location, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.IsFile)
            {
                var local = System.IO.Path.GetFullPath(uri?.IsFile == true ? uri.LocalPath : location);
                if (!File.Exists(local)) throw new FileNotFoundException(null, local);
                return new MediaResource(local, false);
            }

            if (uri.Scheme is not ("http" or "https"))
                throw new NotSupportedException(UiText.Get("UnsupportedMediaLocation"));
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var resource = new MediaResource(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"RitsukageGif-media-{Guid.NewGuid():N}.tmp"), true);
            try
            {
                await using (var input =
                             await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(resource.Path, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return resource;
            }
            catch
            {
                resource.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!temporary) return;
            try
            {
                File.Delete(Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Temporary media cleanup failed: {0}", error.Message);
            }
        }
    }
}
