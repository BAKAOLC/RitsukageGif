using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class CaptureHistoryRegionResolver
    {
        internal static bool TryResolve(SelectionResult saved, IReadOnlyList<WindowTarget> targets,
            PixelRect screenBounds, out SelectionResult? resolved)
        {
            resolved = null;
            try
            {
                using var original = saved.Shape is { } shape ? SKPath.ParseSvgPathData(shape.PathData) : new();
                if (original is null) return false;
                if (saved.Shape is null) original.AddRect(ToSkia(saved.Region));
                else original.FillType = saved.Shape.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                var multi = saved.Windows is { Count: > 1 };
                WindowTarget? primary = null;
                WindowTarget[]? windows = null;
                using var result = new SKPath();
                if (saved.WindowSource || multi)
                {
                    var oldWindows = multi ? saved.Windows! : saved.Target is { } target ? [target] : [];
                    if (oldWindows.Count == 0) return false;
                    windows = new WindowTarget[oldWindows.Count];
                    var handles = new HashSet<nint>();
                    for (var i = 0; i < oldWindows.Count; i++)
                    {
                        var old = oldWindows[i];
                        var matches = targets.Where(current => old.Handle != 0
                            ? current.Handle == old.Handle && current.Process == old.Process
                            : !string.IsNullOrWhiteSpace(old.Process) && !string.IsNullOrWhiteSpace(old.Title) &&
                              current.Process == old.Process && current.Title == old.Title).Take(2).ToArray();
                        if (matches.Length != 1 || !handles.Add(matches[0].Handle)) return false;
                        windows[i] = matches[0];
                        if (old == saved.Target) primary = matches[0];
                    }

                    if (saved.WindowSource && primary is null) return false;
                    primary ??= windows[0];
                    for (var i = 0; i < windows.Length; i++)
                    {
                        var previous = saved.WindowSource ? oldWindows[i].Client : oldWindows[i].Bounds;
                        var current = saved.WindowSource ? windows[i].Client : windows[i].Bounds;
                        using var previousClip = new SKPath();
                        previousClip.AddRect(ToSkia(previous));
                        using var piece = original.Op(previousClip, SKPathOp.Intersect);
                        if (piece is null) return false;
                        piece.Transform(SKMatrix.CreateTranslation(current.X - (float)previous.X,
                            current.Y - (float)previous.Y));
                        using var currentClip = new SKPath();
                        currentClip.AddRect(ToSkia(saved.WindowSource ? current : current.Intersect(screenBounds)));
                        using var clipped = piece.Op(currentClip, SKPathOp.Intersect);
                        if (clipped is null || !result.Op(clipped, SKPathOp.Union, result)) return false;
                    }
                }
                else
                {
                    using var clip = new SKPath();
                    clip.AddRect(ToSkia(screenBounds));
                    if (!original.Op(clip, SKPathOp.Intersect, result)) return false;
                }

                if (result.IsEmpty) return false;
                var bounds = result.TightBounds;
                var left = Math.Floor(bounds.Left);
                var top = Math.Floor(bounds.Top);
                var right = Math.Ceiling(bounds.Right);
                var bottom = Math.Ceiling(bounds.Bottom);
                if (!double.IsFinite(left + top + right + bottom) || left < int.MinValue || top < int.MinValue ||
                    right > int.MaxValue || bottom > int.MaxValue || right - left > int.MaxValue ||
                    bottom - top > int.MaxValue)
                    return false;
                var region = new PixelRect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
                if (region.IsEmpty) return false;
                resolved = new(region, saved.WindowSource, primary, saved.Cursor,
                    saved.Shape is not null || multi
                        ? new(region, result.ToSvgPathData(), result.FillType == SKPathFillType.EvenOdd)
                        : null,
                    multi ? windows : null);
                return true;
            }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            {
                return false;
            }
        }

        private static SKRect ToSkia(PixelRect region)
        {
            return new(region.X, region.Y, region.Right, region.Bottom);
        }
    }
}
