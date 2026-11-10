namespace RitsukageGif.Desktop

{
    internal sealed class RecordingReviewAnimationSource(
        string path,
        int width,
        int height,
        double duration,
        bool hdr,
        CancellationToken lifetime)
        : RecordingReviewSource

    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IEnumerator<PreviewFrame>? _frames;

        private PreviewFrame? _current;

        private long _startTicks, _endTicks;

        private bool _disposed;

        internal override int Width => width;

        internal override int Height => height;

        internal override double Duration => duration;
        internal override bool IsHdr => hdr;

        internal override async Task<PreviewFrame> FrameAtAsync(double position, CancellationToken token)

        {
            await _gate.WaitAsync(token);

            try

            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return await Task.Run(() => Read(position, token), token).ConfigureAwait(false);
            }

            finally

            {
                _gate.Release();
            }
        }

        private PreviewFrame Read(double position, CancellationToken token)

        {
            var ticks = TimeSpan.FromSeconds(position).Ticks;
            if (_frames is null || ticks < _startTicks)

            {
                _current?.Dispose();

                _current = null;

                _frames?.Dispose();

                _frames = PreviewDecoder.ReadRawHdr(path, lifetime).GetEnumerator();

                _startTicks = _endTicks = 0;
            }

            while (_current is null || ticks >= _endTicks)

            {
                token.ThrowIfCancellationRequested();

                if (!_frames.MoveNext()) break;

                _current?.Dispose();

                _current = _frames.Current;

                _startTicks = _endTicks;

                _endTicks += Math.Max(1, _current.Delay) * TimeSpan.TicksPerMillisecond;
            }

            return _current?.Retain() ?? throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
        }

        public override async ValueTask DisposeAsync()

        {
            await _gate.WaitAsync();

            try

            {
                if (_disposed) return;

                _disposed = true;

                _current?.Dispose();

                _current = null;

                _frames?.Dispose();

                _frames = null;
            }

            finally

            {
                _gate.Release();
            }
        }
    }
}
