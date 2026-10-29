using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas
    {
        private (Point Left, Point Right) ArrowTips(Point start, Point end)
        {
            var direction = new Vector(end.X - start.X, end.Y - start.Y);
            var distance = direction.Length;
            if (distance < .001) return (end, end);
            direction /= distance;
            var length = Math.Min(distance * .5, Math.Max(12, Math.Clamp(BrushSize, 1, 1024) * 4));
            var neck = end - direction * length;
            var side = new Vector(-direction.Y, direction.X) * (length * .5);
            return (neck + side, neck - side);
        }
    }
}
