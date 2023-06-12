using AnimatedGif;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CaptureGif
{
    public static class Gif
    {
        public static void Begin(string path, Rectangle rectangle, int delay, int scale, bool cursor, CancellationToken ct, out RecordInfo info)
        {
            var _info = new RecordInfo();
            info = _info;
            BlockingCollection<Frame> bitmaps = new BlockingCollection<Frame>(1000);
            int recordFrames = 0;
            int processedFrames = 0;
            Task task1 = Task.Run(() =>
            {
                int innerDelay = delay;
                Stopwatch sw = new Stopwatch();
                sw.Start();
                using (ScreenFrameProvider provider = new ScreenFrameProvider(rectangle))
                {
                    TimeSpan prev = sw.Elapsed;
                    while (!ct.IsCancellationRequested)
                    {
                        innerDelay = delay;
                        var curr = sw.Elapsed;
                        int usedt = (int)(curr - prev).TotalMilliseconds;
                        if (usedt < innerDelay)
                        {
                            int a = innerDelay - usedt;
                            Sleep(a);
                        }
                        else
                        {
                            innerDelay = usedt;
                        }
                        prev = curr;
                        Bitmap img = provider.Capture(cursor);
                        bitmaps.Add(new Frame()
                        {
                            Bitmap = img,
                            Delay = innerDelay
                        });
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _info.ElapsedSeconds = (int)sw.Elapsed.TotalSeconds;
                            _info.Frames = ++recordFrames;
                        });
                    }
                    bitmaps.CompleteAdding();
                }
                sw.Stop();
            }, ct);
            Task task2 = Task.Run(() =>
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
                        if (frame != null)
                        {
                            gifCreator.AddFrame(frame.Bitmap, delay: frame.Delay, quality: GifQuality.Bit8);
                            frame.Bitmap.Dispose();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _info.ProcessedFrames = ++processedFrames;
                            });
                        }
                    }
                }
            }, ct);
            Task.WaitAll(task1, task2);
        }

        private static void Sleep(int ms)
        {
            var sw = Stopwatch.StartNew();
            var sleepMs = ms - 16;
            if (sleepMs > 0)
            {
                Thread.Sleep(sleepMs);
            }
            while (sw.ElapsedMilliseconds < ms)
            {
                Thread.SpinWait(1);
            }
        }

        public class RecordInfo : NotifyPropertyChanged
        {
            private string _path;
            public string Path
            {
                get => _path;
                set => Set(ref _path, value);
            }

            private bool _recording = false;
            public bool Recording {
                get => _recording;
                set => Set(ref _recording, value);
            }

            private int _elapsedSeconds;
            public int ElapsedSeconds
            {
                get => _elapsedSeconds;
                set => Set(ref _elapsedSeconds, value);
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
