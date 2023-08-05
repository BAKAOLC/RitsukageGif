using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using AnimatedGif;
using RitsukageGif.Capture.ScreenFrameProvider;

namespace RitsukageGif.Capture.RecordFrameProvider
{
    public sealed class GifRecordFrameProvider : IRecordFrameProvider
    {
        public string GetFileExtension()
        {
            return ".gif";
        }

        public RecordInfo BeginWithMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken)
        {
            var info = new RecordInfo
            {
                Path = path
            };
            var bitmaps = new BlockingCollection<GifFrame>(1000);
            var provider = new BitbltScreenFrameProvider(rectangle);
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
                    bitmaps.Add(new GifFrame
                    {
                        Bitmap = img,
                        Delay = (int)dt
                    }, processingToken);
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

                bitmaps.CompleteAdding();
                sw.Stop();
            }, recordingToken);
            sw.Start();
            Task.Run(async () =>
            {
                using (var gifCreator = AnimatedGif.AnimatedGif.Create(path, delay))
                {
                    while (!bitmaps.IsCompleted)
                    {
                        GifFrame frame = null;
                        try
                        {
                            frame = bitmaps.Take();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        if (frame == null) continue;
                        await gifCreator.AddFrameAsync(frame.Bitmap, frame.Delay, GifQuality.Bit8,
                                processingToken)
                            .ConfigureAwait(false);
                        frame.Bitmap.Dispose();
                        info.ProcessedFrames = ++processedFrames;
                    }
                }

                info.Completed = true;
            }, processingToken);
            return info;
        }

        public RecordInfo BeginWithoutMemory(string path, Rectangle rectangle, int delay, double scale,
            bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken)
        {
            var info = new RecordInfo
            {
                Path = path
            };
            var provider = new BitbltScreenFrameProvider(rectangle);
            var lastMilliseconds = 0L;
            var recordFrames = 0;
            var processedFrames = 0;
            var sw = new Stopwatch();
            Task.Run(() =>
            {
                using (var gifCreator = AnimatedGif.AnimatedGif.Create(path, delay))
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
                            gifCreator.AddFrame(img, (int)dt, GifQuality.Bit8);
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
                }

                sw.Stop();
                info.Completed = true;
            }, recordingToken);
            sw.Start();
            return info;
        }
    }
}