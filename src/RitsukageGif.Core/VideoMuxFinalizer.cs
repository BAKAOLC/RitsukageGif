using System.Diagnostics;
using System.Globalization;

namespace RitsukageGif.Core
{
    internal static class VideoMuxFinalizer
    {
        internal static async Task TrimAudioAsync(string executable, string path, VideoContainer container,
            double duration, CancellationToken token)
        {
            var output = path + ".mux";
            using var process = new Process();
            process.StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", path,
                         "-map", "0", "-t", duration.ToString("R", CultureInfo.InvariantCulture), "-c", "copy",
                     })
                process.StartInfo.ArgumentList.Add(argument);
            if (container == VideoContainer.Mp4)
            {
                process.StartInfo.ArgumentList.Add("-movflags");
                process.StartInfo.ArgumentList.Add("+faststart");
            }

            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add(container == VideoContainer.Mp4 ? "mp4" : "webm");
            process.StartInfo.ArgumentList.Add(output);
            token.ThrowIfCancellationRequested();
            process.Start();
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(UiText.Format("FfmpegFailed", process.ExitCode, await errors));
                token.ThrowIfCancellationRequested();
                File.Move(output, path, true);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                await errors;
                if (File.Exists(output)) File.Delete(output);
            }
        }
    }
}
