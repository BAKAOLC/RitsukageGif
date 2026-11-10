using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingCameraDrawOperation(Rect bounds, RecordingCameraSnapshot snapshot, int slot)
        : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) throw new NotSupportedException("Camera preview requires the Skia renderer.");
            var area = snapshot.SlotBounds[slot];
            if (area.IsEmpty || snapshot.Slots[slot] is not { } picture) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.Scale((float)Bounds.Width / area.Width, (float)Bounds.Height / area.Height);
                canvas.Translate(-area.Left, -area.Top);
                canvas.DrawPicture(picture);
            }
            finally
            {
                canvas.Restore();
            }
        }

        public bool HitTest(Point point)
        {
            return Bounds.Contains(point);
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return ReferenceEquals(this, other);
        }

        public void Dispose()
        {
            snapshot.Dispose();
        }
    }
}
