namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureLease : IDisposable
    {
        private static readonly Lock PoolLock = new();
        private static readonly Dictionary<Key, Entry> Pool = [];
        private readonly Entry _entry;
        private readonly Key _key;
        private int _disposed;

        private NativeCaptureLease(Key key, Entry entry)
        {
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var release = false;
            lock (PoolLock)
                if (--_entry.References == 0)
                {
                    if (Pool.TryGetValue(_key, out var entry) && ReferenceEquals(entry, _entry)) Pool.Remove(_key);
                    release = true;
                }

            if (release) _entry.Session.Dispose();
        }

        public static NativeCaptureLease Screen(PixelRect monitor, bool cursor, int fps)
        {
            return Acquire(new(monitor, 0, cursor, Math.Clamp(fps, 1, 120)));
        }

        public static NativeCaptureLease Window(nint window, bool cursor, int fps = 60)
        {
            return Acquire(new(null, window, cursor, Math.Clamp(fps, 1, 120)));
        }

        private static NativeCaptureLease Acquire(Key key)
        {
            lock (PoolLock)
            {
                if (!Pool.TryGetValue(key, out var entry) || !entry.Session.IsAvailable)
                {
                    var session = key.Monitor is { } monitor
                        ? NativeCaptureSession.CreateScreen(monitor, key.Cursor, key.Fps)
                        : NativeCaptureSession.CreateWindow(key.WindowHandle, key.Cursor, key.Fps);
                    Pool[key] = entry = new(session);
                }

                entry.References++;
                return new(key, entry);
            }
        }

        public CaptureSourceFrame Read(CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _entry.Session.Read(token);
        }

        public void UseFrame(CaptureFrameConsumer consume, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _entry.Session.UseFrame(consume, token);
        }

        public static void CloseAll()
        {
            Entry[] entries;
            lock (PoolLock)
            {
                entries = [.. Pool.Values];
                Pool.Clear();
            }

            foreach (var entry in entries) entry.Session.Dispose();
        }

        private sealed record Key(PixelRect? Monitor, nint WindowHandle, bool Cursor, int Fps);

        private sealed class Entry(NativeCaptureSession session)
        {
            public int References;
            public NativeCaptureSession Session { get; } = session;
        }
    }
}
