namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal NativeCaptureSessionReference? CommitSourceReference(CancellationToken token)
        {
            if (!DesktopCapture.UsesSourceSelection)
            {
                CommitSource(token);
                return null;
            }

            var selection = _sourceSelection ?? throw new CaptureSourceLostException();
            var reference = selection.RetainSource();
            try
            {
                selection.Commit(token);
                return reference;
            }
            catch
            {
                reference.Dispose();
                throw;
            }
        }
    }
}
