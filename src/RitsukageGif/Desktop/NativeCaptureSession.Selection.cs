namespace RitsukageGif.Desktop
{
    internal sealed partial class NativeCaptureSession
    {
        public static async Task<NativeCaptureSelection> BeginSelectionAsync(bool window, bool cursor = true,
            int fps = 60, bool retainHdr = false,
            CancellationToken token = default)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await CreateSelectionAsync(window, cursor, fps, retainHdr, token).ConfigureAwait(false);
            }
            finally
            {
                SelectionLock.Release();
            }
        }

        public static async Task<CaptureSourceFrame> PrepareAsync(bool window, bool cursor = true, int fps = 60,
            bool retainHdr = false,
            CancellationToken token = default)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                NativeCaptureSession? current;
                lock (SharedLock) current = _shared;
                if (current is not null &&
                    (current._cursor == cursor || current._capabilities is { SeparateCursor: true }) &&
                    current._fps >= fps
                    && current.IsWindowSource == window && current._retainHdr == retainHdr && current.IsAvailable)
                    return current.Read(token);

                using var selection =
                    await CreateSelectionAsync(window, cursor, fps, retainHdr, token).ConfigureAwait(false);
                selection.Commit(token);
                return selection.Frame;
            }
            finally
            {
                SelectionLock.Release();
            }
        }

        private static async Task<NativeCaptureSelection> CreateSelectionAsync(bool window, bool cursor, int fps,
            bool retainHdr,
            CancellationToken token)
        {
            if (!DesktopCapture.UsesSourceSelection)
                throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            long version;
            lock (SharedLock) version = _sharedVersion;
            token.ThrowIfCancellationRequested();
            var session = new NativeCaptureSession(window ? "window" : "screen", cursor, fps, retainHdr: retainHdr);
            try
            {
                await session._first.Task.WaitAsync(TimeSpan.FromSeconds(155), token).ConfigureAwait(false);
                var frame = session.ReadSnapshot(token: token);
                await session.SuspendWhenIdleAsync(token).ConfigureAwait(false);
                return new(session, version, frame);
            }
            catch (Exception error)
            {
                session.Dispose();
                if (error is TimeoutException) throw new IOException(UiText.Get("CaptureSourceTimeout"), error);
                throw;
            }
        }

        internal static NativeCaptureSession? CommitSelection(NativeCaptureSession candidate, long version,
            CancellationToken token)
        {
            lock (SharedLock)
            {
                token.ThrowIfCancellationRequested();
                if (_sharedVersion != version || !candidate.IsAvailable) throw new CaptureSourceLostException();
                var previous = _shared;
                _shared = candidate;
                _sharedVersion++;
                return previous;
            }
        }
    }
}
