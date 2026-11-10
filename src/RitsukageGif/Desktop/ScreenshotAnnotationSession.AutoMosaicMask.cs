using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private ScreenshotTool RememberedMosaicTool => StyleFor(ScreenshotTool.Mosaic).Properties.MosaicMode switch
        {
            AnnotationMosaicMode.Blur => ScreenshotTool.Blur,
            AnnotationMosaicMode.SmartErase => ScreenshotTool.SmartErase,
            _ => ScreenshotTool.Mosaic,
        };

        private static Rect[] AutoMosaicRectangles(ScreenshotAnnotation annotation)
        {
            if (annotation.AutoMosaicRegions.IsDefaultOrEmpty) return [];
            var bounds = annotation.Bounds;
            return
            [
                .. annotation.AutoMosaicRegions.Select(region => new Rect(
                    bounds.X + region.X * bounds.Width, bounds.Y + region.Y * bounds.Height,
                    region.Width * bounds.Width, region.Height * bounds.Height)),
            ];
        }

        private static ushort[] CreateAutoMosaicCoverage(ScreenshotAnnotation annotation, PixelRect region,
            CancellationToken token)
        {
            var coverage = new ushort[checked(region.Width * region.Height)];
            foreach (var rectangle in AutoMosaicRectangles(annotation))
            {
                var left = Math.Max(region.X, (int)Math.Floor(rectangle.Left));
                var top = Math.Max(region.Y, (int)Math.Floor(rectangle.Top));
                var right = Math.Min(region.Right, (int)Math.Ceiling(rectangle.Right));
                var bottom = Math.Min(region.Bottom, (int)Math.Ceiling(rectangle.Bottom));
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++)
                    {
                        var index = (y - region.Y) * region.Width + x - region.X;
                        if (x >= rectangle.Left && x + 1 <= rectangle.Right &&
                            y >= rectangle.Top && y + 1 <= rectangle.Bottom)
                        {
                            coverage[index] = ushort.MaxValue;
                            continue;
                        }

                        for (var subY = 0; subY < 4; subY++)
                        for (var subX = 0; subX < 4; subX++)
                            if (rectangle.Contains(new Point(x + (subX + .5) / 4, y + (subY + .5) / 4)))
                                coverage[index] |= (ushort)(1 << (subY * 4 + subX));
                    }
                }
            }

            return coverage;
        }

        public void SelectDrawingTool(ScreenshotTool tool)
        {
            if (_disposed || IsBusy || !CanUseDrawingTool(tool)) return;
            if (tool == ScreenshotTool.AutoMosaic)
            {
                RequestPrivacyDetection();
                return;
            }

            tool = tool == ScreenshotTool.Mosaic ? RememberedMosaicTool : tool;

            Tool = tool;
            if (tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase)
            {
                Properties = Properties with
                {
                    MosaicMode = tool switch
                    {
                        ScreenshotTool.Blur => AnnotationMosaicMode.Blur,
                        ScreenshotTool.SmartErase => AnnotationMosaicMode.SmartErase,
                        _ => AnnotationMosaicMode.Mosaic,
                    },
                };
                StoreToolStyle(tool);
                Changed?.Invoke();
            }
        }
    }
}
