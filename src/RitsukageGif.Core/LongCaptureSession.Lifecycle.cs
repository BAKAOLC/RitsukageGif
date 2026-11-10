namespace RitsukageGif.Core
{
    public sealed partial class LongCaptureSession
    {
        public async Task StartAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _lifecycleGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    EnsureWritable();
                    if (_source is null)
                        throw new NotSupportedException("Image feed sessions cannot capture automatically.");
                    if (_running) return;
                }
                finally
                {
                    _operationGate.Release();
                }

                await DrainLoopAsync().ConfigureAwait(false);
                await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    EnsureWritable();
                    _error = null;
                    _running = true;
                    _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
                    _run = Task.Run(() => RunAsync(_runCancellation.Token), CancellationToken.None);
                    Logger.ZLogInformation($"Long capture started");
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task PauseAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            await _lifecycleGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                await DrainLoopAsync().ConfigureAwait(false);
                await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    EnsureAlive();
                    _running = false;
                    if (_source is not null) await _source.SuspendAsync().ConfigureAwait(false);
                    Logger.ZLogInformation($"Long capture paused");
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<ScrollCaptureImage> StopAsync(CancellationToken token = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);
            var operationToken = cancellation.Token;
            await _lifecycleGate.WaitAsync(operationToken).ConfigureAwait(false);
            try
            {
                await DrainLoopAsync().ConfigureAwait(false);
                await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    EnsureAlive();
                    _running = false;
                    _stopped = true;
                    await ReleaseSourceAsync().ConfigureAwait(false);
                    Logger.ZLogInformation($"Long capture stopped");
                    return await Task.Run(() => _session!.ExportPixels(operationToken), operationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposeLock) return new(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            Volatile.Write(ref _disposeRequested, 1);
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Long capture cancellation callback failed");
            }

            await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                await DrainLoopAsync().ConfigureAwait(false);
                await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _running = false;
                    _stopped = true;
                    try
                    {
                        await ReleaseSourceAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        _session = null;
                        _lastResult = null;
                        _disposed = true;
                        _lifetime.Dispose();
                        Logger.ZLogInformation($"Long capture disposed");
                    }
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task DrainLoopAsync()
        {
            if (_runCancellation is not null)
                try
                {
                    await _runCancellation.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    Logger.ZLogError(error, $"Long capture loop cancellation callback failed");
                }

            if (_run is not null) await _run.ConfigureAwait(false);
            _run = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    await _operationGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        var result = await CaptureCoreAsync(token).ConfigureAwait(false);
                        if (result.Status == ScrollCaptureStatus.LimitReached)
                        {
                            _running = false;
                            if (_source is not null) await _source.SuspendAsync().ConfigureAwait(false);
                            Logger.ZLogInformation($"Automatic long capture paused at the stitching limit");
                            return;
                        }
                    }
                    finally
                    {
                        _operationGate.Release();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The lifecycle operation drains this task before releasing the source.
            }
            catch (Exception error)
            {
                await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _running = false;
                    _error = error.Message;
                    Logger.ZLogError(error, $"Automatic long capture failed");
                    if (_source is not null)
                        try
                        {
                            await _source.SuspendAsync().ConfigureAwait(false);
                        }
                        catch (Exception suspendError)
                        {
                            _error = $"{error.Message} Suspend failed: {suspendError.Message}";
                            Logger.ZLogError(suspendError, $"Long capture suspension failed");
                        }
                }
                finally
                {
                    _operationGate.Release();
                }
            }
        }

        private async ValueTask ReleaseSourceAsync()
        {
            var source = _source;
            _source = null;
            if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
        }
    }
}
