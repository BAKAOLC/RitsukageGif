using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class RecognitionDocumentOpener(OpenedDocumentStore store, IDocumentLauncher launcher)
    {
        internal async Task<DocumentOpenResult> OpenAsync(ReadOnlyMemory<byte> encoded, string extension,
            CancellationToken token = default)
        {
            using var lease = await store.CreateAsync(encoded, extension, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lease.Retain();
            try
            {
                await launcher.OpenAsync(lease.Path, token).ConfigureAwait(false);
                Trace.TraceInformation("A generated document was handed to its default application. Format={0}",
                    extension);
                return new(lease.Path, true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                Trace.TraceWarning("The default document application could not be opened. ErrorType={0} Code={1}",
                    error.GetType().Name, error.HResult);
                return new(lease.Path, false, error.GetType().Name);
            }
        }
    }
}
