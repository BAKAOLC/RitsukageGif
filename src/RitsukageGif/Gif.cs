using AnimatedGif;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RitsukageGif
{
    public static class Gif
    {
        public static RecordInfo BeginWithMemory(string path, Rectangle rectangle, int delay, double scale, bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken)
        {
            var info = new RecordInfo
            {
                Path = path
            };
            var bitmaps = new BlockingCollection<Frame>(1000);
            var provider = new ScreenFrameProvider(rectangle);
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
                    bitmaps.Add(new Frame()
                    {
                        Bitmap = img,
                        Delay = (int)dt
                    });
                    Application.Current.Dispatcher.Invoke(() => { info.Frames = ++recordFrames; });
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

                bitmaps.CompleteAdding();
                sw.Stop();
            }, recordingToken);
            sw.Start();
            Task.Run(() =>
            {
                using (var gifCreator = AnimatedGif.AnimatedGif.Create(path, delay))
                {
                    while (!bitmaps.IsCompleted)
                    {
                        Frame frame = null;
                        try
                        {
                            frame = bitmaps.Take();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        if (frame == null) continue;
                        gifCreator.AddFrame(frame.Bitmap, delay: frame.Delay, quality: GifQuality.Bit8);
                        frame.Bitmap.Dispose();
                        Application.Current.Dispatcher.Invoke(() => { info.ProcessedFrames = ++processedFrames; });
                    }

                    Application.Current.Dispatcher.Invoke(() => { info.Completed = true; });
                }
            }, processingToken);
            return info;
        }

        public static RecordInfo BeginWithoutMemory(string path, Rectangle rectangle, int delay, double scale,
            bool cursor,
            CancellationToken recordingToken, CancellationToken processingToken)
        {
            var info = new RecordInfo
            {
                Path = path
            };
            var provider = new ScreenFrameProvider(rectangle);
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
                        Application.Current.Dispatcher.Invoke(() => { info.Frames = ++recordFrames; });
                        gifCreator.AddFrame(img, delay: (int)dt, quality: GifQuality.Bit8);
                        img.Dispose();
                        Application.Current.Dispatcher.Invoke(() => { info.ProcessedFrames = ++processedFrames; });
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
                }

                sw.Stop();
                Application.Current.Dispatcher.Invoke(() => { info.Completed = true; });
            }, recordingToken);
            sw.Start();
            return info;
        }

        public class RecordInfo : NotifyPropertyChanged
        {
            private string _path;

            public string Path
            {
                get => _path;
                set => Set(ref _path, value);
            }

            private int _frames;

            public int Frames
            {
                get => _frames;
                set => Set(ref _frames, value);
            }

            private int _processedFrames;

            public int ProcessedFrames
            {
                get => _processedFrames;
                set => Set(ref _processedFrames, value);
            }

            private bool _completed;

            public bool Completed
            {
                get => _completed;
                set => Set(ref _completed, value);
            }
        }

        class Frame
        {
            public Bitmap Bitmap { get; set; }
            public int Delay { get; set; }
        }
    }
}