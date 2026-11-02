using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class OpenedDocumentLease(string path) : IDisposable
    {
        private bool _disposed;
        private bool _retained;
        internal string Path { get; } = path;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_retained) return;
            try
            {
                File.Delete(Path);
                Directory.Delete(System.IO.Path.GetDirectoryName(Path)!);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Unpublished opened document cleanup was deferred. ErrorType={0} Code={1}",
                    error.GetType().Name, error.HResult);
            }
        }

        internal void Retain()
        {
            _retained = true;
        }
    }
}
