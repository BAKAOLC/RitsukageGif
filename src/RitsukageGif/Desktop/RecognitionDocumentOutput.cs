namespace RitsukageGif.Desktop
{
    internal sealed partial class RecognitionDocumentOutput(
        IFormulaDocumentService formulas,
        ITableOutputService tables,
        RecognitionDocumentOpener opener,
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> write,
        Func<nint, TableRecognitionResult, DocumentOutputOptions, string, CancellationToken, Task<bool>>? printTable =
            null,
        Func<bool>? printAvailable = null,
        Func<CancellationToken, Task<PrintCapabilities>>? printCapabilities = null)
    {
        internal bool CanPrint => printTable is not null && (printAvailable?.Invoke() ?? true);

        internal Task<PrintCapabilities> GetPrintCapabilitiesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (printTable is null) return Task.FromResult(new PrintCapabilities(false));
            return printCapabilities?.Invoke(token) ?? Task.FromResult(new PrintCapabilities(CanPrint));
        }

        internal async Task<bool> PrintTableAsync(nint owner, TableRecognitionResult table,
            DocumentOutputOptions options,
            string title, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var capabilities = await GetPrintCapabilitiesAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!capabilities.Available)
                throw new PlatformNotSupportedException("This document output service does not support printing.");
            return await printTable!(owner, table, options, title, token).ConfigureAwait(false);
        }

        internal async Task SaveFormulaDocxAsync(IReadOnlyList<string> mathMl, string path, CancellationToken token)
        {
            var encoded = await formulas.EncodeDocxAsync(mathMl, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await write(path, encoded, token).ConfigureAwait(false);
        }

        internal async Task<DocumentOpenResult> OpenFormulaDocxAsync(IReadOnlyList<string> mathMl,
            CancellationToken token)
        {
            var encoded = await formulas.EncodeDocxAsync(mathMl, token).ConfigureAwait(false);
            return await opener.OpenAsync(encoded, ".docx", token).ConfigureAwait(false);
        }

        internal async Task<DocumentOpenResult> OpenTableAsync(TableRecognitionResult table, CancellationToken token)
        {
            var encoded = await tables.EncodeAsync(table, TableOutputFormat.Xlsx, token).ConfigureAwait(false);
            return await opener.OpenAsync(encoded, ".xlsx", token).ConfigureAwait(false);
        }
    }
}
