namespace RitsukageGif.Core
{
    internal sealed class AnimationEditingDocument(AnimationEditDocument document) : IAnimationDocument
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _disposed;
        private AnimationDocumentInfo _current = Snapshot(document);

        public AnimationDocumentInfo Current => Volatile.Read(ref _current);
        public bool IsClosed => Volatile.Read(ref _disposed);

        public Task SetDelayAsync(IReadOnlyList<int> indices, int delayMs, CancellationToken token = default)
        {
            return EditAsync(() => document.SetDelay(indices, delayMs), token);
        }

        public Task SetIterationsAsync(uint iterations, CancellationToken token = default)
        {
            return EditAsync(() => document.SetIterations(iterations), token);
        }

        public Task DuplicateAsync(IReadOnlyList<int> indices, CancellationToken token = default)
        {
            return EditAsync(() => document.Duplicate(indices), token);
        }

        public Task DeleteAsync(IReadOnlyList<int> indices, CancellationToken token = default)
        {
            return EditAsync(() => document.Delete(indices), token);
        }

        public Task MoveAsync(int from, int to, CancellationToken token = default)
        {
            return EditAsync(() => document.Move(from, to), token);
        }

        public Task AddBlankAsync(int insertIndex, int delayMs, string color, CancellationToken token = default)
        {
            return EditAsync(() => document.AddBlank(insertIndex, delayMs, color), token);
        }

        public Task ImportAsync(string path, int insertIndex, CancellationToken token = default)
        {
            return EditAsync(() => document.Import(path, insertIndex, token), token);
        }

        public Task UndoAsync(CancellationToken token = default)
        {
            return EditAsync(document.Undo, token);
        }

        public Task RedoAsync(CancellationToken token = default)
        {
            return EditAsync(document.Redo, token);
        }

        public Task TransformAsync(MediaProcessOptions options, CancellationToken token = default)
        {
            return EditAsync(() => document.Transform(options, token), token);
        }

        public Task ApplyEffectsAsync(IReadOnlyList<int> indices, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default)
        {
            return EditAsync(() => document.ApplyEffects(indices, effects, token), token);
        }

        public Task OverlayAsync(IReadOnlyList<int> indices, string path, int x, int y,
            CancellationToken token = default)
        {
            return EditAsync(() => document.Overlay(indices, path, x, y, token), token);
        }

        public Task ReplaceFramesAsync(IReadOnlyList<int> indices, byte[] image, CancellationToken token = default)
        {
            return EditAsync(() => document.ReplaceFrames(indices, image, token), token);
        }

        public Task ExportFrameAsync(int index, string path, CancellationToken token = default)
        {
            return EditAsync(() => document.ExportFrame(index, path, token), token);
        }

        public Task<int[]> MoveSelectedAsync(IReadOnlyList<int> indices, int direction,
            CancellationToken token = default)
        {
            return InvokeAsync(() => Task.Run(() => document.MoveSelected(indices, direction), token), token);
        }

        public Task ExportAsync(string path, MediaProcessOptions options, CancellationToken token = default)
        {
            return InvokeAsync(async () =>
            {
                await document.ExportAsync(path, options, token: token).ConfigureAwait(false);
                return true;
            }, token);
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                await Task.Run(document.Dispose).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static AnimationDocumentInfo Snapshot(AnimationEditDocument value)
        {
            var frames = value.Frames;
            var snapshot = new AnimationFrameInfo[frames.Count];
            for (var index = 0; index < snapshot.Length; index++)
            {
                var frame = frames[index];
                snapshot[index] = new(frame.Width, frame.Height, frame.DelayMs);
            }

            return new(value.Width, value.Height, value.Iterations, value.CanUndo, value.CanRedo,
                Array.AsReadOnly(snapshot));
        }

        private async Task<T> InvokeAsync<T>(Func<Task<T>> operation, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (!_disposed) Volatile.Write(ref _current, Snapshot(document));
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        private Task<bool> EditAsync(Action operation, CancellationToken token)
        {
            return InvokeAsync(async () =>
            {
                await Task.Run(operation, token).ConfigureAwait(false);
                return true;
            }, token);
        }
    }
}
