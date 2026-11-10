using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingCameraPreviewControl(int slot) : Control, IDisposable
    {
        private RecordingCameraSnapshot? _snapshot;

        public void Dispose()
        {
            _snapshot?.Dispose();
            _snapshot = null;
        }

        internal void Update(RecordingCameraSnapshot snapshot)
        {
            var previous = _snapshot;
            _snapshot = snapshot.Retain();
            previous?.Dispose();
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_snapshot is { } snapshot)
                context.Custom(new RecordingCameraDrawOperation(new(Bounds.Size), snapshot.Retain(), slot));
        }
    }
}
