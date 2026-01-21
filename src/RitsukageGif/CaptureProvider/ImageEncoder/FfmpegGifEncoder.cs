using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RitsukageGif.Class;

namespace RitsukageGif.CaptureProvider.ImageEncoder
{
    public class FfmpegGifEncoder(string filePath) : IImageEncoder
    {
        private readonly List<(string Path, int DelayMs)> _frames = [];

        public Task AddFrameAsync(string path, int delayMs, CancellationToken token)
        {
            // GIF delay unit is 10ms, round to nearest 10ms
            var delay = Math.Max(10, (delayMs + 5) / 10 * 10);
            _frames.Add((path, delay));
            return Task.CompletedTask;
        }

        public Task FinalizeAsync(CancellationToken token)
        {
            try
            {
                if (_frames.Count == 0) return Task.CompletedTask;

                // Create concat file
                var concatFilePath = Path.GetTempFileName();
                var concatLines = new List<string>();

                foreach (var frame in _frames)
                {
                    var durationSec = frame.DelayMs / 1000.0;
                    concatLines.Add($"file '{frame.Path.Replace("\\", "/").Replace("'", "'\\''")}'");
                    concatLines.Add($"duration {durationSec:F3}");
                }

                // Add last frame again for concat demuxer
                if (_frames.Count > 0)
                    concatLines.Add($"file '{_frames[^1].Path.Replace("\\", "/").Replace("'", "'\\''")}'");

                File.WriteAllLines(concatFilePath, concatLines);

                var args = $"-f concat -safe 0 -i \"{concatFilePath}\" " +
                           $"-vf \"split[s0][s1];[s0]palettegen=max_colors=256[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5\" " +
                           $"-loop 0 -y \"{filePath}\"";

                var process = new Process
                {
                    StartInfo = new()
                    {
                        FileName = FfmpegHelper.FfmpegPath ?? "ffmpeg",
                        Arguments = args,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                process.WaitForExit();

                // Clean up
                try
                {
                    File.Delete(concatFilePath);
                }
                catch
                {
                    // ignored
                }
            }
            catch
            {
                // Ignore errors during encoding
            }

            return Task.CompletedTask;
        }
    }
}