using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RitsukageGif.Core
{
    public static class FfmpegEncoder
    {
        public static string? FindExecutable()
        {
            foreach (var directory in new[] { AppContext.BaseDirectory }.Concat(
                         (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var path = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(path)) return path;
            }

            return null;
        }

        public static async Task EncodeAsync(string executable, IReadOnlyList<StoredFrame> frames, string output,
            RecordingOptions options, CancellationToken token)
        {
            if (options.Format == OutputFormat.Gif)
            {
                var timing = new GifTiming(options.Fps);
                var sampled = new List<StoredFrame>();
                foreach (var frame in frames)
                {
                    token.ThrowIfCancellationRequested();
                    var delay = timing.Next(frame.DelayMs);
                    while (delay > 0)
                    {
                        var part = Math.Min(65535, delay);
                        if (delay - part == 1) part--;
                        sampled.Add(frame with { DelayMs = part * 10 });
                        delay -= part;
                    }
                }

                frames = sampled;
            }

            if (frames.Count == 0) throw new ArgumentException(UiText.Get("NoInputFrames"));
            var manifest = output + ".ffconcat";
            var palette = output + ".palette.png";
            try
            {
                var lines = new List<string> { "ffconcat version 1.0" };
                foreach (var frame in frames)
                {
                    lines.Add($"file '{frame.Path.Replace("\\", "/").Replace("'", "'\\''")}'");
                    lines.Add("option framerate 1000");
                    lines.Add("duration " + (frame.DelayMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture));
                }

                lines.Add($"file '{frames[^1].Path.Replace("\\", "/").Replace("'", "'\\''")}'");
                lines.Add("option framerate 1000");
                await File.WriteAllLinesAsync(manifest, lines, new UTF8Encoding(false), token);
                string[] input = ["-f", "concat", "-safe", "0", "-i", manifest];
                if (options.Format == OutputFormat.Gif)
                {
                    // Two passes avoid split/palettegen retaining the whole movie in RAM.
                    await RunAsync(executable,
                        [
                            .. input, "-vf",
                            "palettegen=stats_mode=diff:reserve_transparent=" + (options.Transparent ? "1" : "0"),
                            "-frames:v", "1", palette
                        ],
                        token);
                    var dither = options.Quality switch
                    {
                        GifQuality.Fast => "bayer:bayer_scale=3",
                        GifQuality.Best => "sierra2_4a",
                        _ => "floyd_steinberg"
                    };
                    await RunAsync(executable, [
                        .. input, "-i", palette, "-lavfi",
                        "paletteuse=dither=" + dither + ":diff_mode=rectangle", "-fps_mode", "vfr",
                        "-frames:v", frames.Count.ToString(CultureInfo.InvariantCulture), "-final_delay",
                        (frames[^1].DelayMs / 10).ToString(CultureInfo.InvariantCulture),
                        "-loop", "0", "-f", "gif", output
                    ], token);
                }
                else
                {
                    string[] encoding = options.Format == OutputFormat.WebP
                        ? ["-c:v", "libwebp_anim", "-lossless", "1", "-loop", "0", "-f", "webp"]
                        :
                        [
                            "-frames:v", frames.Count.ToString(CultureInfo.InvariantCulture), "-final_delay",
                            $"{frames[^1].DelayMs}/1000", "-plays", "0", "-f", "apng"
                        ];
                    await RunAsync(executable, [.. input, "-fps_mode", "vfr", .. encoding, output], token);
                    if (options.Format == OutputFormat.WebP)
                        CorrectWebpDuration(output, frames.Sum(f => (long)f.DelayMs));
                }
            }
            finally
            {
                if (File.Exists(manifest)) File.Delete(manifest);
                if (File.Exists(palette)) File.Delete(palette);
            }
        }

        // libwebp_anim merges the sentinel frame but estimates its duration from the
        // average frame rate. Correct the final ANMF hold to the measured recording time.
        private static void CorrectWebpDuration(string path, long expectedMilliseconds)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            Span<byte> header = stackalloc byte[12];
            file.ReadExactly(header);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WEBP"u8))
                throw new InvalidDataException(UiText.Get("InvalidWebp"));
            long total = 0, lastPosition = -1;
            var lastDelay = 0;
            Span<byte> chunk = stackalloc byte[8];
            Span<byte> delay = stackalloc byte[3];
            while (file.Position + 8 <= file.Length)
            {
                file.ReadExactly(chunk);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                var start = file.Position;
                if (length > file.Length - start) throw new InvalidDataException(UiText.Get("TruncatedWebp"));
                if (chunk[..4].SequenceEqual("ANMF"u8) && length >= 16)
                {
                    lastPosition = start + 12;
                    file.Position = lastPosition;
                    file.ReadExactly(delay);
                    lastDelay = delay[0] | (delay[1] << 8) | (delay[2] << 16);
                    total += lastDelay;
                }

                file.Position = start + length + (length & 1);
            }

            if (lastPosition < 0) return; // A single static frame has no animation timeline.
            var corrected = lastDelay + expectedMilliseconds - total;
            if (corrected is < 1 or > 0xffffff) throw new InvalidDataException(UiText.Get("WebpTimingRange"));
            delay[0] = (byte)corrected;
            delay[1] = (byte)(corrected >> 8);
            delay[2] = (byte)(corrected >> 16);
            file.Position = lastPosition;
            file.Write(delay);
        }

        private static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(executable)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" }.Concat(arguments))
                process.StartInfo.ArgumentList.Add(argument);
            token.ThrowIfCancellationRequested();
            process.Start();
            var errors = process.StandardError.ReadToEndAsync(token);
            try
            {
                await process.WaitForExitAsync(token);
                var error = await errors;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(UiText.Format("FfmpegFailed", process.ExitCode, error));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                try
                {
                    await errors;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Cancellation also interrupts a pipe retained by a descendant process.
                }
            }
        }
    }
}
