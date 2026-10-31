using ImageMagick;

namespace RitsukageGif.Core
{
    internal static class MagickImageCloner
    {
        internal static IMagickImage<byte> Clone(IMagickImage<byte> source, CancellationToken token = default)
        {
            var clone = source.Clone();
            try
            {
                // Native clones inherit a callback pointer, but not the managed delegate that owns it.
                clone.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                return clone;
            }
            catch
            {
                clone.Dispose();
                throw;
            }
        }
    }
}
