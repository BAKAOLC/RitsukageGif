namespace RitsukageGif.Desktop
{
    internal sealed record SelectionResult(
        PixelRect Region,
        bool WindowSource,
        WindowTarget? Target,
        bool Cursor = false)
    {
        internal static SelectionResult? Create(PixelRect region, bool windowSource, WindowTarget? target,
            bool sourceLocal = false, bool cursor = false)
        {
            if (windowSource && !sourceLocal)
            {
                if (target is null) return null;
                region = region.Intersect(target.Client);
            }

            return region.IsEmpty ? null : new SelectionResult(region, windowSource, target, cursor);
        }

        internal WindowPreset? CreatePreset()
        {
            if (!WindowSource || Target is null) return null;
            return WindowPreset.ForWindow(Target.Process, Target.Title) with
            {
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
