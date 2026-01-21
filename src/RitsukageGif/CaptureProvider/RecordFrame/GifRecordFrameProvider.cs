using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using RitsukageGif.CaptureProvider.ImageEncoder;
using RitsukageGif.CaptureProvider.ScreenFrame;
using RitsukageGif.Enums;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public sealed class AnimatedRecordFrameProvider : IRecordFrameProvider
    {
        private OutputFormat _currentFormat = OutputFormat.Gif;

        public string GetFileExtension()
        {
            return ImageEncoderFactory.GetFileExtension(_currentFormat);
        }

        public RecordInfo BeginWithMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken, OutputFormat format = OutputFormat.Gif)
        {
            _currentFormat = format;
            var info = new RecordInfo
            {
                Path = path,
            };
            var bitmaps = new BlockingCollection<AnimatedFrame>(1000);
            var provider = ScreenFrameProvider.CreateProvider();
            provider.ApplyCaptureRegion(rectangle);
            var lastMilliseconds = 0L;
            var recordFrames = 0;
            var processedFrames = 0;
            var sw = new Stopwatch();
            Task.Run(() =>
            {
                while (!recordingToken.IsCancellationRequested)
                {
                    var t = sw.ElapsedMilliseconds;
                    var dt = t - lastMilliseconds;
                    lastMilliseconds = t;
                    var img = provider.Capture(cursor, scale);
                    bitmaps.Add(new(img, (int)dt), processingToken);
                    if (!processingToken.IsCancellationRequested)
                    {
                        info.Frames = ++recordFrames;
                        if (dt > delay)
                        {
                            var d = (int)(delay - (dt - delay));
                            Thread.Sleep(d > 0 ? d : 1);
                        }
                        else
                        {
                            Thread.Sleep(delay);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                provider.Dispose();
                bitmaps.CompleteAdding();
                sw.Stop();
            }, recordingToken);
            sw.Start();
            Task.Run(async () =>
            {
                using (var encoder = ImageEncoderFactory.CreateEncoder(format, path))
                {
                    while (!bitmaps.IsCompleted)
                    {
                        AnimatedFrame frame = null;
                        try
                        {
                            frame = bitmaps.Take();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        if (frame == null) continue;
                        await encoder.AddFrameAsync(frame.Bitmap, frame.Delay, processingToken)
                            .ConfigureAwait(false);
                        frame.Bitmap.Dispose();
                        info.ProcessedFrames = ++processedFrames;
                    }

                    encoder.Finish();
                }

                info.Completed = true;
            }, processingToken);
            return info;
        }

        public RecordInfo BeginWithoutMemory(string path, Rectangle rectangle, int delay, double scale,
            bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken, OutputFormat format = OutputFormat.Gif)
        {
            _currentFormat = format;
            var info = new RecordInfo
            {
                Path = path,
            };
            var provider = ScreenFrameProvider.CreateProvider();
            provider.ApplyCaptureRegion(rectangle);
            var lastMilliseconds = 0L;
            var recordFrames = 0;
            var processedFrames = 0;
            var sw = new Stopwatch();
            Task.Run(async () =>
            {
                using (var encoder = ImageEncoderFactory.CreateEncoder(format, path))
                {
                    while (!recordingToken.IsCancellationRequested)
                    {
                        var t = sw.ElapsedMilliseconds;
                        var dt = t - lastMilliseconds;
                        lastMilliseconds = t;
                        var img = provider.Capture(cursor, scale);
                        info.Frames = ++recordFrames;
                        if (!processingToken.IsCancellationRequested)
                        {
                            await encoder.AddFrameAsync(img, (int)dt, processingToken).ConfigureAwait(false);
                            img.Dispose();
                            info.ProcessedFrames = ++processedFrames;
                            if (dt > delay)
                            {
                                var d = (int)(delay - (dt - delay));
                                Thread.Sleep(d > 0 ? d : 1);
                            }
                            else
                            {
                                Thread.Sleep(delay);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    encoder.Finish();
                }

                sw.Stop();
                provider.Dispose();
                info.Completed = true;
            }, processingToken);
            sw.Start();
            return info;
        }
    }
}