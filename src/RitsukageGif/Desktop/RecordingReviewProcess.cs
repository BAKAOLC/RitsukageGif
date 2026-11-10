using System.Diagnostics;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class RecordingReviewProcess
    {
        internal static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments,
            CancellationToken token, Action<string>? outputLine = null)
        {
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("Media process could not be started.");
            await using var registration = token.Register(() =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!process.HasExited)
                        // ReSharper disable once AccessToDisposedClosure
                        process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }
            });
            var output = DrainAsync(process.StandardOutput, outputLine);
            var error = DrainAsync(process.StandardError);
            try
            {
                var exited = process.WaitForExitAsync(CancellationToken.None);
                if (await Task.WhenAny(exited, output).ConfigureAwait(false) == output)
                    await output.ConfigureAwait(false);
                await exited.ConfigureAwait(false);
                var result = await output.ConfigureAwait(false);
                var details = await error.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException(details);
                return result;
            }
            finally
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
        }

        private static async Task<string> DrainAsync(StreamReader reader, Action<string>? outputLine = null)
        {
            var result = new StringBuilder();
            var line = outputLine is null ? null : new StringBuilder();
            var buffer = new char[2048];
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
            {
                if (result.Length + read > 16384) result.Remove(0, Math.Min(result.Length, read));
                result.Append(buffer, 0, read);
                if (line is null) continue;
                for (var index = 0; index < read; index++)
                    if (buffer[index] != '\n')
                    {
                        line.Append(buffer[index]);
                    }
                    else
                    {
                        outputLine!(line.ToString().TrimEnd('\r'));
                        line.Clear();
                    }
            }

            if (line is { Length: > 0 }) outputLine!(line.ToString());
            return result.ToString();
        }
    }
}
