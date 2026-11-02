using System.Diagnostics;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class OcrProcessRunner
    {
        internal static async Task<OcrProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
            Func<Stream, CancellationToken, Task>? writeInput, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("The OCR process could not start.");
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                IOException? inputError = null;
                try
                {
                    if (writeInput is not null)
                        await writeInput(process.StandardInput.BaseStream, token).ConfigureAwait(false);
                }
                catch (IOException failure)
                {
                    inputError = failure;
                }
                finally
                {
                    process.StandardInput.Close();
                }

                await process.WaitForExitAsync(token).ConfigureAwait(false);
                var result = new OcrProcessResult(process.ExitCode, await output.ConfigureAwait(false),
                    await error.ConfigureAwait(false));
                if (inputError is not null && result.ExitCode == 0)
                    throw new IOException("The OCR process did not accept the complete image.", inputError);
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                throw;
            }
        }

        internal static async Task WritePpmAsync(Stream destination, byte[] bgra, int width, int height,
            CancellationToken token)
        {
            var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            await destination.WriteAsync(header, token).ConfigureAwait(false);
            var row = new byte[checked(width * 3)];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var source = (y * width + x) * 4;
                    row[x * 3] = bgra[source + 2];
                    row[x * 3 + 1] = bgra[source + 1];
                    row[x * 3 + 2] = bgra[source];
                }

                await destination.WriteAsync(row, token).ConfigureAwait(false);
            }
        }
    }
}
