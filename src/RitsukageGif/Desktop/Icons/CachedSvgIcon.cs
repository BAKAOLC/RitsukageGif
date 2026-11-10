using Avalonia;

namespace RitsukageGif.Desktop.Icons
{
    internal sealed class CachedSvgIcon
    {
        internal CachedSvgIcon(IconDefinition definition)
        {
            Paths = [.. definition.Paths.Select(static path => new CachedSvgPath(path))];
            var viewport = new Rect(definition.MinX, definition.MinY, definition.Width, definition.Height);
            foreach (var path in Paths)
            {
                if (path.Definition.Fill) viewport = viewport.Union(path.Geometry.Bounds);
                if (path.Stroke is not null) viewport = viewport.Union(path.Geometry.GetRenderBounds(path.Stroke));
            }

            Viewport = viewport;
        }

        internal CachedSvgPath[] Paths { get; }
        private Rect Viewport { get; }

        internal Matrix GetTransform(Size target)
        {
            var scale = Math.Min(target.Width / Viewport.Width, target.Height / Viewport.Height);
            return new(scale, 0, 0, scale,
                (target.Width - Viewport.Width * scale) / 2 - Viewport.X * scale,
                (target.Height - Viewport.Height * scale) / 2 - Viewport.Y * scale);
        }
    }
}
