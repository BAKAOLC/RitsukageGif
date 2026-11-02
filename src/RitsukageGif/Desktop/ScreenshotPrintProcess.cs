using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotPrintProcess
    {
        internal static Task<bool> PrintAsync(nint owner, byte[] image, string title, CancellationToken token,
            Func<ProcessStartInfo>? startInfoFactory = null)
        {
            if (!OperatingSystem.IsWindows())
                return NativePrintProcess.PrintImageAsync(image, title, token, startInfoFactory);
            ArgumentNullException.ThrowIfNull(image);
            return PrintWorkerTransport.RunAsync(owner, title, "--print-worker", async (directory, cancellation) =>
            {
                var input = Path.Combine(directory, "input.png");
                await File.WriteAllBytesAsync(input, image, cancellation).ConfigureAwait(false);
                return input;
            }, startInfoFactory ?? Program.CreateSelfStartInfo, null, token);
        }

        internal static Task<bool> PrintTableAsync(nint owner, TableRecognitionResult table,
            DocumentOutputOptions options, string title, ITablePrintRenderer renderer,
            Func<ProcessStartInfo>? startInfoFactory, CancellationToken token)
        {
            if (!OperatingSystem.IsWindows())
                return NativePrintProcess.PrintTableAsync(table, options, title, renderer, token, startInfoFactory);
            token.ThrowIfCancellationRequested();
            var snapshot = table with { Cells = [.. table.Cells] };
            return PrintWorkerTransport.RunAsync(owner, title, "--table-print-worker",
                (directory, _) => Task.FromResult(directory), startInfoFactory ?? Program.CreateSelfStartInfo,
                async (directory, response, cancellation) =>
                {
                    var metrics = JsonSerializer.Deserialize(response,
                                      DocumentPrintJsonContext.Default.PrinterPageMetrics)
                                  ?? throw new InvalidDataException("The printer returned no paper geometry.");
                    var pages = await renderer.RenderAsync(snapshot, metrics.Apply(options), cancellation)
                        .ConfigureAwait(false);
                    return await DocumentPrintInput
                        .WriteAsync(directory, DocumentPrintLayout.Snapshot(pages), cancellation)
                        .ConfigureAwait(false);
                }, token);
        }

        internal static int RunTableWorker(string directory, nint owner, int parentProcessId, string title,
            Func<nint, string, Func<PrinterPageMetrics, CancellationToken, IReadOnlyList<PrintPageFile>>,
                CancellationToken, bool>? print = null)
        {
            var pages = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            return PrintWorkerTransport.RunWorker(parentProcessId,
                token =>
                {
                    if (!Directory.Exists(directory))
                        throw new DirectoryNotFoundException("The print session directory is missing.");
                    return (print ?? ScreenshotPrintService.PrintSelectedNative)(owner, title,
                        (metrics, cancellation) =>
                        {
                            Console.WriteLine(JsonSerializer.Serialize(metrics,
                                DocumentPrintJsonContext.Default.PrinterPageMetrics));
                            var manifest = pages.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
                            if (Path.GetFullPath(manifest) != Path.Combine(Path.GetFullPath(directory), "pages.json"))
                                throw new InvalidDataException("The print worker received an unexpected manifest.");
                            return DocumentPrintInput.Read(manifest);
                        }, token);
                },
                () => DiagnosticLog.Initialize("print-worker"), DiagnosticLog.Shutdown,
                line => pages.TrySetResult(line));
        }

        internal static int RunWorker(string input, nint owner, int parentProcessId, string title)
        {
            return PrintWorkerTransport.RunWorker(parentProcessId,
                token => ScreenshotPrintService.PrintFileNative(owner, input, title, token),
                () => DiagnosticLog.Initialize("print-worker"), DiagnosticLog.Shutdown);
        }
    }
}
