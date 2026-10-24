using ImageMagick;

namespace RitsukageGif.Core
{
    internal static partial class MediaPngCodec
    {
        internal static MagickImageCollection ReadFrame(string path, int frameIndex, MediaInfo info,
            CancellationToken token)
        {
            if (frameIndex < 0 || frameIndex >= info.Frames)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            token.ThrowIfCancellationRequested();
            var poster = HasPoster(path, token);
            var iterations = ReadIterations(path);
            token.ThrowIfCancellationRequested();
            using var canvas = new MagickImage(MagickColors.Transparent, (uint)info.Width, (uint)info.Height);
            canvas.SetArtifact("compose:outside-overlay", "false");
            canvas.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var index = 0;
            try
            {
                foreach (var frame in PngAnimation.ReadFrames(path, token))
                    using (frame)
                    {
                        token.ThrowIfCancellationRequested();
                        if (poster)
                        {
                            poster = false;
                            continue;
                        }

                        using var previous = index != frameIndex && frame.GifDisposeMethod == GifDisposeMethod.Previous
                            ? canvas.Clone()
                            : null;
                        canvas.Composite(frame, frame.Page.X, frame.Page.Y, frame.Compose);
                        token.ThrowIfCancellationRequested();
                        if (index++ == frameIndex)
                        {
                            var selected = canvas.Clone();
                            try
                            {
                                selected.AnimationDelay = 0;
                                selected.AnimationTicksPerSecond = frame.AnimationTicksPerSecond;
                                selected.AnimationIterations = iterations;
                                selected.GifDisposeMethod = GifDisposeMethod.Background;
                                selected.ResetPage();
                                return new MagickImageCollection { selected };
                            }
                            catch
                            {
                                selected.Dispose();
                                throw;
                            }
                        }

                        if (previous is not null) canvas.Composite(previous, CompositeOperator.Src);
                        else if (frame.GifDisposeMethod == GifDisposeMethod.Background)
                        {
                            using var empty = new MagickImage(MagickColors.Transparent, frame.Width, frame.Height);
                            canvas.Composite(empty, frame.Page.X, frame.Page.Y, CompositeOperator.Src);
                        }
                    }

                throw new InvalidDataException();
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
        }

        private static bool HasPoster(string path, CancellationToken token)
        {
            using var stream = File.OpenRead(path);
            stream.Position = Signature.Length;
            var animated = false;
            foreach (var (type, _) in ReadChunks(stream, true))
            {
                token.ThrowIfCancellationRequested();
                if (type == "acTL") animated = true;
                else if (type == "fcTL") return false;
                else if (type == "IDAT") return animated;
            }

            return false;
        }
    }
}
