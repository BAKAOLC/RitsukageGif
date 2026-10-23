namespace RitsukageGif.Desktop
{
    internal sealed partial class NativeCaptureSession
    {
        public static async Task<NativeCaptureSelection> BeginSelectionAsync(bool window, CancellationToken token,
            bool cursor = true, int fps = 60)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await CreateSelectionAsync(window, token, cursor, fps).ConfigureAwait(false);
            }
            finally
            {
                SelectionLock.Release();
            }
        }

        public static async Task<CaptureSourceFrame> PrepareAsync(bool window, CancellationToken token,
            bool cursor = true, int fps = 60)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                NativeCaptureSession? current;
                lock (SharedLock) current = _shared;
                if (current is not null && current._cursor == cursor && current._fps >= fps
                    && current._window == window && current.IsAvailable)
                    return current.Read(token);

                using var selection = await CreateSelectionAsync(window, token, cursor, fps).ConfigureAwait(false);
                selection.Commit(token);
                return selection.Frame;
            }
            finally
            {
                SelectionLock.Release();
            }
        }

        private static async Task<NativeCaptureSelection> CreateSelectionAsync(bool window, CancellationToken token,
            bool cursor, int fps)
        {
            if (!DesktopCapture.UsesSourceSelection)
                throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            long version;
            lock (SharedLock) version = _sharedVersion;
            token.ThrowIfCancellationRequested();
            var session = new NativeCaptureSession(window ? "window" : "screen", cursor, fps);
            try
            {
                await session._first.Task.WaitAsync(TimeSpan.FromSeconds(155), token).ConfigureAwait(false);
                return new NativeCaptureSelection(session, version, session.Read(token));
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
