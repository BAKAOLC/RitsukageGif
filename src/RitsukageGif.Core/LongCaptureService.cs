namespace RitsukageGif.Core
{
    public sealed class LongCaptureService(
        Func<LongCaptureOptions, CancellationToken, Task<ILongCaptureFrameSource>>? sourceFactory = null)
        : ILongCaptureService
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.LongCapture");
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Lock _disposal = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly List<ILongCaptureSession> _sessions = [];
        private bool _disposed;
        private Task? _disposeTask;

        public async Task<ILongCaptureSession> CreateAsync(LongCaptureOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _gate.WaitAsync(work.Token).ConfigureAwait(false);
            ILongCaptureFrameSource? source = null;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                LongCaptureSession.ValidateOptions(options);
                if (options.Source != LongCaptureSourceKind.Frames)
                {
                    if (sourceFactory is null) throw new NotSupportedException("Live long capture is unavailable.");
                    source = await sourceFactory(options, work.Token).ConfigureAwait(false);
                }

                work.Token.ThrowIfCancellationRequested();
                var session = new LongCaptureSession(options, source);
                source = null;
                _sessions.Add(session);
                return session;
            }
            catch (Exception)
            {
                if (source is not null)
                    try
                    {
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        Logger.ZLogError(error, $"Long capture source cleanup after creation failure failed");
                    }

                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposal) return new(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            List<Exception>? failures = null;
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                for (var index = _sessions.Count - 1; index >= 0; index--)
                    try
                    {
                        await _sessions[index].DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }

                _sessions.Clear();
                if (failures is not null) throw new AggregateException(failures);
            }
            finally
            {
                _gate.Release();
                _lifetime.Dispose();
            }
        }
    }
}
