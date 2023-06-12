using System;
using System.Drawing;
using System.Linq;

namespace CaptureGif
{
    public class SelectedRegionResult
    {
        public Rectangle OriginalRectangle { get; }

        public ScreenRegion[] Regions { get; }

        public SelectedRegionResult(Rectangle originalRectangle, ScreenRegion[] regions)
        {
            OriginalRectangle = originalRectangle;
            Regions = regions;
        }

        public override string ToString()
        {
            return
                $"OriginalRectangle: {OriginalRectangle}{Environment.NewLine}Regions: {Environment.NewLine}{string.Join(Environment.NewLine, Regions.Select(x => x.ToString()))}";
        }
    }
}
