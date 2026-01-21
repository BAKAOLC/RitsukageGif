using System;
using System.Drawing;
using System.Linq;

namespace RitsukageGif.Class
{
    public class SelectedRegionResult
    {
        public SelectedRegionResult(Rectangle originalRectangle, ScreenRegion[] regions)
        {
            Original = originalRectangle;
            Regions = regions;
            if (regions != null && regions.Length != 0)
            {
                var x = (int)regions.Min(r => r.Rectangle.X);
                var y = (int)regions.Min(r => r.Rectangle.Y);
                var right = (int)regions.Max(r => r.Rectangle.Right);
                var bottom = (int)regions.Max(r => r.Rectangle.Bottom);
                Converted = new(x, y, right - x, bottom - y);
            }
            else
            {
                Converted = originalRectangle;
            }
        }

        public Rectangle Original { get; }

        public Rectangle Converted { get; }

        public ScreenRegion[] Regions { get; }

        public override string ToString()
        {
            return
                $"Original: {Original}{Environment.NewLine}Converted: {Converted}{Environment.NewLine}Regions: {Environment.NewLine}{string.Join(Environment.NewLine, Regions.Select(x => x.ToString()))}";
        }
    }
}