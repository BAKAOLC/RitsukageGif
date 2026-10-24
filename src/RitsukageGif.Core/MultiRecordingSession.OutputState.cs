using System.Threading.Channels;

namespace RitsukageGif.Core
{
    public sealed partial class MultiRecordingSession
    {
        private sealed class OutputState : IDisposable, IProgress<RecordingProgress>
        {
            private const long MemoryLimit = 64L * 1024 * 1024;
            private const long BacklogDiskLimit = 4L * 1024 * 1024 * 1024;

            private readonly Channel<QueuedFrame> _queue = Channel.CreateBounded<QueuedFrame>(
                new BoundedChannelOptions(100000)
                    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

            private readonly IProgress<MultiRecordingProgress>? _progress;
            private readonly CancellationTokenSource _abort;
            private readonly string _directory;
            private readonly string _staging;
            private readonly bool _live;
            private long _memory;
            private long _disk;
            private int _captured;
            private int _encoded;
            private double _last;
            private double _end;
            private int _completed;
            private Exception? _failure;

            public OutputState(MultiRecordingOutput output, IProgress<MultiRecordingProgress>? progress,
                CancellationToken cancel)
            {
                Output = output;
                _progress = progress;
                _abort = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                _directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", Guid.NewGuid().ToString("N"));
                var destination = Path.GetFullPath(output.Path);
                // Same-directory staging ensures publication remains an atomic rename across volumes.
                _staging = Path.Combine(Path.GetDirectoryName(destination)!, "." + Guid.NewGuid().ToString("N") +
                                                                             "." + output.Options.Format.ToString()
                                                                                 .ToLowerInvariant());
                _live = !(output.Options.UseFfmpeg && FfmpegEncoder.FindExecutable() is not null);
                Next = 1000.0 / output.Options.Fps;
            }

            public void Prepare()
            {
                Directory.CreateDirectory(_directory);
                Directory.CreateDirectory(Path.GetDirectoryName(_staging)!);
            }

            public MultiRecordingOutput Output { get; }
            public IFrameSource? Source { get; set; }
            public Task Worker { get; set; } = Task.CompletedTask;
            public Task EnqueueWorker { get; private set; } = Task.CompletedTask;
            public double Next { get; set; }
            public Exception? Failure => Volatile.Read(ref _failure);

            public void Enqueue(byte[] pixels, double time)
            {
                var write = EnqueueAsync(pixels, time, _abort.Token);
                if (write.IsCompletedSuccessfully) write.GetAwaiter().GetResult();
                else EnqueueWorker = FinishEnqueueAsync(write);
            }

            private async Task FinishEnqueueAsync(ValueTask write)
            {
                try
                {
                    await write;
                }
                catch (Exception error)
                {
                    Fail(error);
                }
            }

            public async ValueTask EnqueueAsync(byte[] pixels, double time, CancellationToken token)
            {
                if (pixels.Length != checked(Output.Options.Width * Output.Options.Height * 4))
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
                QueuedFrame frame;
                if (Interlocked.Add(ref _memory, pixels.Length) <= MemoryLimit)
                    frame = new QueuedFrame(pixels, null, time);
                else
                {
                    Interlocked.Add(ref _memory, -pixels.Length);
                    ReserveBacklogDisk(pixels.Length);
                    var path = Path.Combine(_directory, $"raw-{_captured:D8}.bgra");
                    await File.WriteAllBytesAsync(path, pixels, token);
                    frame = new QueuedFrame(null, path, time);
                }

                Volatile.Write(ref _last, time);
                Interlocked.Increment(ref _captured);
                if (!_queue.Writer.TryWrite(frame)) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                Report(new RecordingProgress(RecordingStage.Recording, 0, Volatile.Read(ref _encoded)));
            }

            private void ReserveBacklogDisk(long bytes)
            {
                if (Interlocked.Add(ref _disk, bytes) > BacklogDiskLimit)
                {
                    Interlocked.Add(ref _disk, -bytes);
                    throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                }
            }

            public void Complete(double end)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                _end = Math.Max(end, Volatile.Read(ref _last));
                _queue.Writer.TryComplete();
            }

            public void Fail(Exception error)
            {
                var first = Interlocked.CompareExchange(ref _failure, error, null) is null;
                _abort.Cancel();
                _queue.Writer.TryComplete();
                if (!first) return;
                try
                {
                    _progress?.Report(new MultiRecordingProgress(Output.Name,
                        new RecordingProgress(RecordingStage.Recording, Volatile.Read(ref _captured),
                            Volatile.Read(ref _encoded), ElapsedSeconds: Volatile.Read(ref _last) / 1000),
                        error.Message));
                }
                catch (Exception reportError)
                {
                    System.Diagnostics.Trace.TraceInformation("Progress reporting failed: {0}", reportError.Message);
                }
            }

            public Task EncodeAsync() => Task.Run(EncodeCoreAsync);

            private async Task EncodeCoreAsync()
            {
                try
                {
                    if (_live)
                        await StreamingAnimationEncoder.EncodeAsync(ReadFrames(), _staging, Output.Options, this,
                            _abort.Token);
                    else
                    {
                        var frames = new List<StoredFrame>();
                        await foreach (var raw in ReadFrames())
                        {
                            // Completed frame storage is limited by the volume, not the live backlog budget.
                            var path = Path.Combine(_directory, $"frame-{frames.Count:D8}.bmp");
                            await BitmapSpool.WriteAsync(path, raw.Pixels, Output.Options.Width, Output.Options.Height,
                                _abort.Token);
                            frames.Add(new StoredFrame(path, raw.DelayMs));
                        }

                        await AnimationEncoder.EncodeAsync(frames, _staging, Output.Options, this, _abort.Token);
                    }

                    _abort.Token.ThrowIfCancellationRequested();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }
            }

            public void Publish()
            {
                if (Failure is not null) return;
                try
                {
                    File.Move(_staging, Output.Path, false);
                    ReportComplete();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }
            }

            private async IAsyncEnumerable<RawFrame> ReadFrames()
            {
                QueuedFrame? pending = null;
                await foreach (var frame in _queue.Reader.ReadAllAsync(_abort.Token))
                {
                    if (pending is { } previous)
                        yield return new RawFrame(await LoadAsync(previous), Delay(previous.Time, frame.Time));
                    pending = frame;
                }

                if (pending is { } last)
                    yield return new RawFrame(await LoadAsync(last), Delay(last.Time, _end));
            }

            private static int Delay(double from, double to) => Math.Max(1, (int)(Math.Round(to) - Math.Round(from)));

            private async Task<byte[]> LoadAsync(QueuedFrame frame)
            {
                if (frame.Pixels is { } pixels)
                {
                    Interlocked.Add(ref _memory, -pixels.Length);
                    return pixels;
                }

                var data = await File.ReadAllBytesAsync(frame.Path!, _abort.Token);
                File.Delete(frame.Path!);
                Interlocked.Add(ref _disk, -data.Length);
                return data;
            }

            private void ReportComplete() => ReportProgress(new RecordingProgress(RecordingStage.Complete, 0,
                Volatile.Read(ref _encoded)));

            public void Report(RecordingProgress value)
            {
                if (value.Stage != RecordingStage.Recording)
                    value = value with
                    {
                        Stage = Volatile.Read(ref _completed) == 0 ? RecordingStage.Live : RecordingStage.Encoding
                    };
                ReportProgress(value);
            }

            private void ReportProgress(RecordingProgress value)
            {
                if (value.Stage != RecordingStage.Recording) Volatile.Write(ref _encoded, value.Encoded);
                var count = Volatile.Read(ref _captured);
                var last = Volatile.Read(ref _last);
                _progress?.Report(new MultiRecordingProgress(Output.Name, value with
                {
                    Captured = count,
                    Encoded = Volatile.Read(ref _encoded),
                    CapturedFps = last > 0 ? (count - 1) * 1000 / last : 0,
                    ElapsedSeconds = (Volatile.Read(ref _completed) == 0 ? last : _end) / 1000
                }));
            }

            public MultiRecordingResult Result() => new(Output.Name, Output.Path, _captured, _encoded,
                _end / 1000, Failure?.Message);

            public void Dispose()
            {
                try
                {
                    Source?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceInformation("Capture disposal failed: {0}", ex.Message);
                }

                _abort.Dispose();
                while (_queue.Reader.TryRead(out _))
                {
                }

                try
                {
                    File.Delete(_staging);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                try
                {
                    Directory.Delete(_directory, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private readonly record struct QueuedFrame(byte[]? Pixels, string? Path, double Time);
    }
}
