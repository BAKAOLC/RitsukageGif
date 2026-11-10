using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingCameraSnapshot(SKPicture picture, SKPicture?[] slots, SKRect[] bounds) : IDisposable
    {
        private int _references = 1;
        internal SKPicture Picture { get; } = picture;
        internal IReadOnlyList<SKPicture?> Slots { get; } = slots;
        internal IReadOnlyList<SKRect> SlotBounds { get; } = bounds;

        internal SKRect AffectedArea { get; } = bounds.Aggregate(SKRect.Empty,
            (area, next) => area.IsEmpty ? next : next.IsEmpty ? area : SKRect.Union(area, next));

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _references) != 0) return;
            Picture.Dispose();
            foreach (var slot in Slots) slot?.Dispose();
        }

        internal RecordingCameraSnapshot Retain()
        {
            Interlocked.Increment(ref _references);
            return this;
        }
    }
}
