namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        internal static NativeCaptureSessionReference? RetainStaticSource(CaptureSourceKind? source,
            out bool window)
        {
            lock (SharedLock)
            {
                window = source is { } kind ? kind == CaptureSourceKind.Window : _shared?.IsWindowSource == true;
                return _shared is { } session && session.IsWindowSource == window ? session.Retain() : null;
            }
        }

        internal static async Task<NativeCaptureSessionReference> PrepareStaticSourceAsync(
            NativeCaptureSession? expected, bool window, bool cursor, int fps, bool retainHdr,
            CancellationToken token)
        {
            await SelectionLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                NativeCaptureGeometry? geometry = null;
                if (expected is not null)
                    lock (expected._gate)
                    {
                        if (!expected.IsAvailable || expected._sourceReason is not null)
                            throw new CaptureSourceLostException();
                        if (expected._cursor == cursor || expected._capabilities is { SeparateCursor: true })
                            return expected.Retain();
                        geometry = expected._sourceGeometry;
                    }

                using var selection =
                    await CreateSelectionAsync(window, cursor, fps, retainHdr, token).ConfigureAwait(false);
                var source = selection.RetainSource();
                try
                {
                    if (expected is not null)
                        lock (source.Session._gate)
                        {
                            var next = source.Session._sourceGeometry;
                            if (geometry is not { StableIdentity: true } || next is not { StableIdentity: true } ||
                                geometry.SourceId != next.SourceId || geometry.Bounds != next.Bounds)
                                throw new CaptureSourceLostException();
                        }
                    else
                        selection.Commit(token);

                    return source;
                }
                catch
                {
                    source.Dispose();
                    throw;
                }
            }
            finally
            {
                SelectionLock.Release();
            }
        }
    }
}
