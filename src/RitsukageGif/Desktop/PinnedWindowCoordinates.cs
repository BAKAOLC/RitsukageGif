using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static class PinnedWindowCoordinates
    {
        internal static Point PointToClient(Window window, Control frame, Visual visual,
            Avalonia.PixelPoint screenPoint)
        {
            if (window.IsVisible) return visual.PointToClient(screenPoint);
            var client = new Point((screenPoint.X - window.Position.X) / window.RenderScaling,
                (screenPoint.Y - window.Position.Y) / window.RenderScaling);
            return frame.TranslatePoint(client, visual)!.Value;
        }

        internal static Avalonia.PixelPoint PointToScreen(Window window, Control frame, Visual visual, Point point)
        {
            if (window.IsVisible) return visual.PointToScreen(point);
            var client = visual.TranslatePoint(point, frame)!.Value;
            return new(window.Position.X + (int)Math.Round(client.X * window.RenderScaling),
                window.Position.Y + (int)Math.Round(client.Y * window.RenderScaling));
        }
    }
}
