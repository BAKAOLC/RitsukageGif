using ImageMagick;

namespace RitsukageGif.Core
{
    internal static partial class MediaPngCodec
    {
        // The yielded frame belongs to this iterator and is disposed on the next MoveNext or Dispose.
        internal static IEnumerable<IMagickImage<byte>> ReadCoalescedFrames(string path, MediaInfo info,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var poster = HasPoster(path, token);
            var iterations = ReadIterations(path);
            using var canvas = new MagickImage(MagickColors.Transparent, (uint)info.Width, (uint)info.Height);
            canvas.SetArtifact("compose:outside-overlay", "false");
            canvas.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var count = 0;
            foreach (var frame in PngAnimation.ReadFrames(path, token))
                using (frame)
                {
                    token.ThrowIfCancellationRequested();
                    if (poster)
                    {
                        poster = false;
                        continue;
                    }

                    using var previous = frame.GifDisposeMethod == GifDisposeMethod.Previous ? canvas.Clone() : null;
                    canvas.Composite(frame, frame.Page.X, frame.Page.Y, frame.Compose);
                    using var complete = canvas.Clone();
                    complete.AnimationDelay = frame.AnimationDelay;
                    complete.AnimationTicksPerSecond = frame.AnimationTicksPerSecond;
                    complete.AnimationIterations = iterations;
                    complete.GifDisposeMethod = GifDisposeMethod.Background;
                    complete.ResetPage();
                    count++;
                    yield return complete;

                    token.ThrowIfCancellationRequested();
                    if (previous is not null)
                    {
                        canvas.Composite(previous, CompositeOperator.Src);
                    }
                    else if (frame.GifDisposeMethod == GifDisposeMethod.Background)
                    {
                        using var empty = new MagickImage(MagickColors.Transparent, frame.Width, frame.Height);
                        canvas.Composite(empty, frame.Page.X, frame.Page.Y, CompositeOperator.Src);
                    }
                }

            if (count != info.Frames) throw new InvalidDataException();
        }
    }
}
