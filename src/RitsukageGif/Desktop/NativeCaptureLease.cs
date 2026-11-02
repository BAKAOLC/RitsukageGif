namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureLease : IDisposable
    {
        private static readonly Lock PoolLock = new();
        private static readonly Dictionary<Key, Entry> Pool = [];
        private readonly Entry _entry;
        private readonly Key _key;
        private readonly NativeCaptureDemand _demand;
        private int _disposed;

        private NativeCaptureLease(Key key, Entry entry)
        {
            _key = key;
            _entry = entry;
            _demand = new(() => entry.Session);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _demand.Dispose();
            var release = false;
            lock (PoolLock)
                if (--_entry.References == 0)
                {
                    if (Pool.TryGetValue(_key, out var entry) && ReferenceEquals(entry, _entry)) Pool.Remove(_key);
                    release = true;
                }

            if (release) _entry.Session.Dispose();
        }

        public static NativeCaptureLease Screen(PixelRect monitor, bool cursor, int fps, bool retainHdr = false)
        {
            return Acquire(new(monitor, 0, cursor, Math.Clamp(fps, 1, 120), retainHdr));
        }

        public static NativeCaptureLease Window(nint window, bool cursor, int fps = 60, bool retainHdr = false)
        {
            return Acquire(new(null, window, cursor, Math.Clamp(fps, 1, 120), retainHdr));
        }

        private static NativeCaptureLease Acquire(Key key)
        {
            lock (PoolLock)
            {
                if (!Pool.TryGetValue(key, out var entry) || !entry.Session.IsAvailable)
                {
                    var session = key.Monitor is { } monitor
                        ? NativeCaptureSession.CreateScreen(monitor, key.Cursor, key.Fps, key.RetainHdr)
                        : NativeCaptureSession.CreateWindow(key.WindowHandle, key.Cursor, key.Fps, key.RetainHdr);
                    Pool[key] = entry = new(session);
                }

                entry.References++;
                return new(key, entry);
            }
        }

        public NativeCaptureSnapshot ReadSnapshot(bool requireHdr = false, PixelRect? region = null,
            double? density = null, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            NativeCaptureSnapshot? result = null;
            _demand.Use(value => result = value with
            {
                Frames = value.Frames with
                {
                    Sdr = value.Frames.Sdr with { Pixels = value.Frames.Sdr.Pixels.ToArray() },
                    Hdr = value.Frames.Hdr is { } hdr ? hdr with { Pixels = hdr.Pixels.ToArray() } : null,
                },
                CursorPixels = value.CursorPixels?.ToArray(),
            }, region, density, requireHdr, token);
            return result!;
        }

        public void UseSnapshot(Action<NativeCaptureSnapshot> consume, bool requireHdr = false,
            PixelRect? region = null, double? density = null, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _demand.Use(consume, region, density, requireHdr, token);
        }

        internal void SuspendDemand()
        {
            _demand.Dispose();
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

        private sealed record Key(PixelRect? Monitor, nint WindowHandle, bool Cursor, int Fps, bool RetainHdr);

        private sealed class Entry(NativeCaptureSession session)
        {
            public int References;
            public NativeCaptureSession Session { get; } = session;
        }
    }
}
