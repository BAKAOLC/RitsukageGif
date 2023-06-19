using System;
using System.Drawing;
using System.Linq;

namespace RitsukageGif
{
    public class SelectedRegionResult
    {
        public Rectangle Original { get; }

        public Rectangle Converted { get; }

        public ScreenRegion[] Regions { get; }

        public SelectedRegionResult(Rectangle originalRectangle, ScreenRegion[] regions)
        {
            Original = originalRectangle;
            Regions = regions;
            if (regions != null && regions.Any())
            {
                int x = (int)regions.Min(r => r.Rectangle.X);
                int y = (int)regions.Min(r => r.Rectangle.Y);
                int right = (int)regions.Max(r => r.Rectangle.Right);
                int bottom = (int)regions.Max(r => r.Rectangle.Bottom);
                Converted = new Rectangle(x, y, right - x, bottom - y);
            }
            else
            {
                Converted = originalRectangle;
            }
        }

        public override string ToString()
        {
            return
                $"Original: {Original}{Environment.NewLine}Converted: {Converted}{Environment.NewLine}Regions: {Environment.NewLine}{string.Join(Environment.NewLine, Regions.Select(x => x.ToString()))}";
        }
    }
}