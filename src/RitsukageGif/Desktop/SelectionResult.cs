namespace RitsukageGif.Desktop
{
    internal sealed record SelectionResult(PixelRect Region, bool WindowSource, WindowTarget? Target)
    {
        internal static SelectionResult? Create(PixelRect region, bool windowSource, WindowTarget? target,
            bool sourceLocal = false)
        {
            if (windowSource && !sourceLocal)
            {
                if (target is null) return null;
                region = region.Intersect(target.Client);
            }

            return region.IsEmpty ? null : new SelectionResult(region, windowSource, target);
        }

        internal WindowPreset? CreatePreset()
        {
            if (!WindowSource || Target is not { } target) return null;
            return WindowPreset.ForWindow(target.Process, target.Title) with
            {
                Source = CaptureSourceKind.Window, Follow = true, FixedHandle = target.Handle,
                Area = Region == target.Client
                    ? [new PresetLiteral(0d), new PresetLiteral(0d), new PresetPercent(100), new PresetPercent(100)]
                    :
                    [
                        new PresetLiteral((double)(Region.X - target.Client.X)),
                        new PresetLiteral((double)(Region.Y - target.Client.Y)),
                        new PresetLiteral((double)Region.Width), new PresetLiteral((double)Region.Height)
                    ]
            };
        }
    }
}
