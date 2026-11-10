using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed record SelectionResult(
        PixelRect Region,
        bool WindowSource,
        WindowTarget? Target,
        bool Cursor = false,
        SelectionShape? Shape = null,
        IReadOnlyList<WindowTarget>? Windows = null)
    {
        internal NativeCaptureSession? CaptureSession { get; init; }
        internal string? IncludedCaptureWindowTitle { get; init; }

        internal static SelectionResult? Create(PixelRect region, bool windowSource, WindowTarget? target,
            bool sourceLocal = false, bool cursor = false, SelectionShape? shape = null,
            IReadOnlyList<WindowTarget>? windows = null)
        {
            if (windowSource && !sourceLocal)
            {
                if (target is null) return null;
                if (windows is not { Count: > 1 }) region = region.Intersect(target.Client);
            }

            return region.IsEmpty ? null : new SelectionResult(region, windowSource, target, cursor, shape, windows);
        }

        internal WindowPreset? CreatePreset()
        {
            PresetShape? shape = null;
            if (Shape is not null)
            {
                using var path = SKPath.ParseSvgPathData(Shape.PathData);
                path.Transform(SKMatrix.CreateTranslation(-Region.X, -Region.Y));
                shape = new("path", PathData: path.ToSvgPathData(), EvenOdd: Shape.EvenOdd);
            }

            if (!WindowSource || Target is null)
                return shape is null
                    ? null
                    : WindowPreset.ForWindow("", "") with
                    {
                        Absolute = true, Source = CaptureSourceKind.Screen, Shape = shape,
                        Area =
                        [
                            new PresetLiteral((double)Region.X), new PresetLiteral((double)Region.Y),
                            new PresetLiteral((double)Region.Width), new PresetLiteral((double)Region.Height),
                        ],
                    };
            if (Windows is { Count: > 1 })
            {
                var layers = new List<PresetLayer>();
                foreach (var window in Windows.Reverse())
                {
                    var crop = Region.Intersect(window.Client);
                    if (crop.IsEmpty) continue;
                    PresetShape layerShape = new("rect");
                    if (Shape is not null)
                    {
                        using var path = SKPath.ParseSvgPathData(Shape.PathData);
                        path.Transform(SKMatrix.CreateTranslation(-crop.X, -crop.Y));
                        layerShape = new("path", PathData: path.ToSvgPathData(), EvenOdd: Shape.EvenOdd);
                    }

                    layers.Add(new("window-" + layers.Count, window.Process, window.Title, true,
                        [
                            new PresetLiteral((double)(crop.X - window.Client.X)),
                            new PresetLiteral((double)(crop.Y - window.Client.Y)),
                            new PresetLiteral((double)crop.Width), new PresetLiteral((double)crop.Height),
                        ],
                        new(crop.X - Region.X, crop.Y - Region.Y, crop.Width, crop.Height), layerShape,
                        CaptureSourceKind.Window, new(), FixedHandle: window.Handle));
                }

                return WindowPreset.ForWindow("", "") with
                {
                    Layout = new(Region.Width, Region.Height, "transparent", layers),
                };
            }

            return WindowPreset.ForWindow(Target.Process, Target.Title) with
            {
                Shape = shape,
                Source = CaptureSourceKind.Window, Follow = true, FixedHandle = Target.Handle,
                Area = Region == Target.Client
                    ? [new PresetLiteral(0d), new PresetLiteral(0d), new PresetPercent(100), new PresetPercent(100)]
                    :
                    [
                        new PresetLiteral((double)(Region.X - Target.Client.X)),
                        new PresetLiteral((double)(Region.Y - Target.Client.Y)),
                        new PresetLiteral((double)Region.Width), new PresetLiteral((double)Region.Height),
                    ],
            };
        }
    }
}
