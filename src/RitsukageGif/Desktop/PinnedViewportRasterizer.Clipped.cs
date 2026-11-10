using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedViewportRasterizer
    {
        private static void RenderProjection<TSource, TTarget>(TSource source, TTarget target, Matrix sourceToTarget,
            int width, int height, double opacity, bool smoothScaling, PinnedViewportClip? clip,
            CancellationToken token)
            where TSource : struct, IPinnedViewportSource where TTarget : struct, IPinnedViewportTarget
        {
            if (clip is not { } view)
            {
                Render(source, target, sourceToTarget, width, height, opacity, smoothScaling, token);
                return;
            }

            RenderClamped(source, new PinnedViewportClipTarget<TTarget>(target, width,
                    new(view.SourceDisplayRegion, view.TargetClip, sourceToTarget)), sourceToTarget,
                width, height, opacity, smoothScaling, token);
        }

        private static void RenderClamped<TSource, TTarget>(TSource source, TTarget target, Matrix sourceToTarget,
            int width, int height, double opacity, bool smoothScaling, CancellationToken token)
            where TSource : struct, IPinnedViewportSource where TTarget : struct, IPinnedViewportTarget
        {
            var inverse = sourceToTarget.Invert();
            var footprintX = Math.Abs(inverse.M11) + Math.Abs(inverse.M21);
            var footprintY = Math.Abs(inverse.M12) + Math.Abs(inverse.M22);
            var padding = (int)Math.Ceiling(Math.Max(1, Math.Max(footprintX, footprintY)) / 2) + 1;
            Render(new PinnedViewportClampedSource<TSource>(source, padding), target,
                Matrix.CreateTranslation(-padding, -padding) * sourceToTarget,
                width, height, opacity, smoothScaling, token);
        }
    }
}
