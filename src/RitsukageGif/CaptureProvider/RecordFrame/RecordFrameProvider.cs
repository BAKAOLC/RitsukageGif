using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RitsukageGif.CaptureProvider.ImageEncoder;
using RitsukageGif.CaptureProvider.ScreenFrame;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public sealed class RecordFrameProvider
    {
        private OutputFormat _currentFormat = OutputFormat.Gif;

        public string GetFileExtension()
        {
            return ImageEncoderFactory.GetFileExtension(_currentFormat);
        }

        public void SetOutputFormat(OutputFormat format)
        {
            _currentFormat = format;
        }

        public RecordInfo BeginRecord(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken)
        {
            var format = _currentFormat;
            var info = new RecordInfo
            {
                Path = path,
            };

            var fileName = Path.GetFileNameWithoutExtension(path);
            var tempDir = Path.Combine(MainWindow.TempPath, fileName);
            Directory.CreateDirectory(tempDir);

            var provider = ScreenFrameProvider.CreateProvider();
            provider.ApplyCaptureRegion(rectangle);
            var lastMilliseconds = 0L;
            var recordFrames = 0;
            var sw = new Stopwatch();
            string? lastFrameDelayPath = null; // 保存上一帧的delay文件路径

            Task.Run(async () =>
            {
                try
                {
                    sw.Start();
                    while (!recordingToken.IsCancellationRequested && !processingToken.IsCancellationRequested)
                    {
                        var t = sw.ElapsedMilliseconds;
                        var dt = t - lastMilliseconds;

                        if (lastFrameDelayPath != null && dt > 0)
                            await File.WriteAllTextAsync(lastFrameDelayPath, dt.ToString())
                                .ConfigureAwait(false);

                        lastMilliseconds = t;

                        var img = provider.Capture(cursor, scale);
                        var frameIndex = recordFrames++;
                        info.Frames = recordFrames;

                        var framePath = Path.Combine(tempDir, $"{frameIndex:D6}.png");
                        var delayPath = Path.Combine(tempDir, $"{frameIndex:D6}.frameDuration");

                        img.Save(framePath, ImageFormat.Png);
                        img.Dispose();

                        lastFrameDelayPath = delayPath;

                        if (dt < delay)
                            Thread.Sleep(delay - (int)dt);
                        else
                            Thread.Sleep(1);
                    }

                    if (lastFrameDelayPath != null)
                    {
                        var finalDelay = Math.Max(1, (int)(sw.ElapsedMilliseconds - lastMilliseconds));
                        await File.WriteAllTextAsync(lastFrameDelayPath, finalDelay.ToString())
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    sw.Stop();
                    provider.Dispose();
                }

                if (!processingToken.IsCancellationRequested)
                    await EncodeFramesAsync(tempDir, path, format, info, processingToken).ConfigureAwait(false);

                info.Completed = true;
                CleanupTempFiles(tempDir);
            });

            return info;
        }

        private static async Task EncodeFramesAsync(string tempDir, string outputPath, OutputFormat format,
            RecordInfo info, CancellationToken cancellationToken)
        {
            var frameFiles = Directory.GetFiles(tempDir, "*.png");
            if (frameFiles.Length == 0) return;

            Array.Sort(frameFiles, StringComparer.Ordinal);

            var encoder = ImageEncoderFactory.CreateEncoder(format, outputPath);
            for (var i = 0; i < frameFiles.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var delayFile = Path.ChangeExtension(frameFiles[i], ".frameDuration");
                var delay = 0;
                if (File.Exists(delayFile))
                {
                    var delayText = await File.ReadAllTextAsync(delayFile, cancellationToken).ConfigureAwait(false);
                    if (!int.TryParse(delayText, out delay)) delay = 100;
                }

                await encoder.AddFrameAsync(frameFiles[i], delay, cancellationToken).ConfigureAwait(false);

                info.ProcessedFrames = i + 1;
            }

            if (!cancellationToken.IsCancellationRequested)
                await encoder.FinalizeAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void CleanupTempFiles(string tempDir)
        {
            Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            });
        }
    }
}