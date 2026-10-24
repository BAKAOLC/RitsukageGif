using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class MultiRecordingSession
    {
        private sealed class StopTimestamp(Stopwatch clock)
        {
            private long _ticks = -1;
            public long Ticks => Interlocked.Read(ref _ticks);
            public void Record() => Interlocked.CompareExchange(ref _ticks, clock.ElapsedTicks, -1);
        }

        public Task<IReadOnlyList<MultiRecordingResult>> RunAsync(
            IReadOnlyList<MultiRecordingOutput> outputs, Func<MultiRecordingOutput, IFrameSource> sourceFactory,
            IProgress<MultiRecordingProgress>? progress, CancellationToken stop, CancellationToken cancel,
            Action<double>? beginTick = null) =>
            Task.Run(() => RunCoreAsync(outputs, sourceFactory, progress, stop, cancel, beginTick));

        private static async Task<IReadOnlyList<MultiRecordingResult>> RunCoreAsync(
            IReadOnlyList<MultiRecordingOutput> outputs, Func<MultiRecordingOutput, IFrameSource> sourceFactory,
            IProgress<MultiRecordingProgress>? progress, CancellationToken stop, CancellationToken cancel,
            Action<double>? beginTick)
        {
            if (outputs.Count is < 1 or > 8) throw new ArgumentException(UiText.Get("InvalidRecording"));
            var paths = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var output in outputs)
            {
                output.Options.Validate();
                if (string.IsNullOrWhiteSpace(output.Name) || !names.Add(output.Name) ||
                    !paths.Add(Path.GetFullPath(output.Path)))
                    throw new ArgumentException(UiText.Get("InvalidRecording"));
                if (File.Exists(output.Path)) throw new IOException(output.Path);
            }

            var states = new List<OutputState>();
            using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop, cancel);
            var clock = new Stopwatch();
            var stopTime = new StopTimestamp(clock);
            using var registration = stop.Register(stopTime.Record);
            try
            {
                foreach (var output in outputs)
                    states.Add(new OutputState(output, progress, cancel));
                beginTick?.Invoke(0);
                foreach (var state in states)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (stop.IsCancellationRequested) break;
                    try
                    {
                        state.Prepare();
                        state.Source = sourceFactory(state.Output);
                        var pixels = state.Source.Capture(captureStop.Token);
                        if (captureStop.IsCancellationRequested) break;
                        await state.EnqueueAsync(pixels, 0, cancel);
                    }
                    catch (OperationCanceledException) when (captureStop.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        state.Fail(ex);
                    }
                }

                clock.Start();
                foreach (var state in states) state.Worker = state.EncodeAsync();
                while (!captureStop.IsCancellationRequested)
                {
                    var due = double.PositiveInfinity;
                    foreach (var state in states)
                        if (state.Failure is null)
                            due = Math.Min(due, state.Next);
                    if (double.IsPositiveInfinity(due)) break;
                    var delay = due - clock.Elapsed.TotalMilliseconds;
                    if (delay > 0)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), captureStop.Token);
                        }
                        catch (OperationCanceledException) when (captureStop.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    if (captureStop.IsCancellationRequested) break;
                    var now = clock.Elapsed.TotalMilliseconds;
                    beginTick?.Invoke(now / 1000);
                    foreach (var state in states)
                    {
                        if (captureStop.IsCancellationRequested) break;
                        if (state.Next > now || state.Failure is not null) continue;
                        try
                        {
                            // A slow disk spill belongs to this output. Other outputs keep their
                            // cadence, while this one skips deadlines until its buffer is queued.
                            if (state.EnqueueWorker.IsCompleted)
                            {
                                var pixels = state.Source!.Capture(captureStop.Token);
                                if (captureStop.IsCancellationRequested) break;
                                state.Enqueue(pixels, clock.Elapsed.TotalMilliseconds);
                            }

                            var period = 1000.0 / state.Output.Options.Fps;
                            // Skip missed deadlines instead of generating catch-up frames or slowing playback.
                            state.Next = Math.Max(state.Next + period,
                                (Math.Floor(clock.Elapsed.TotalMilliseconds / period) + 1) * period);
                        }
                        catch (OperationCanceledException) when (captureStop.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            state.Fail(ex);
                        }
                    }
                }

                var ticks = stopTime.Ticks;
                var end = ticks < 0 ? clock.Elapsed.TotalMilliseconds : ticks * 1000.0 / Stopwatch.Frequency;
                foreach (var state in states) await state.EnqueueWorker;
                foreach (var state in states) state.Complete(end);
                await Task.WhenAll(states.Select(state => state.Worker));
                cancel.ThrowIfCancellationRequested();
                // Cancellation is accepted until this commit barrier. Publication is a short synchronous
                // batch; once it begins, return committed per-output results even if cancellation arrives.
                foreach (var state in states) state.Publish();
                return states.Select(state => state.Result()).ToArray();
            }
            catch (Exception ex)
            {
                foreach (var state in states) state.Fail(ex);
                throw;
            }
            finally
            {
                // Never close a queue or release a source while its final disk write is pending.
                foreach (var state in states) await state.EnqueueWorker;
                foreach (var state in states) state.Complete(clock.Elapsed.TotalMilliseconds);
                await Task.WhenAll(states.Select(state => state.Worker));
                foreach (var state in states) state.Dispose();
            }
        }
    }
}
