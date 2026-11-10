using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingCameraLayout
    {
        internal static SKRect Bounds(RecordingCameraSlot slot, int sourceWidth, int sourceHeight,
            int captureWidth, int captureHeight, bool initial)
        {
            var rotated = slot.Rotation % 180 != 0;
            var aspect = slot.Shape == RecordingCameraShape.Circle ? 1d : (double)sourceWidth / sourceHeight;
            var widthFactor = rotated ? 1 : aspect;
            var heightFactor = rotated ? aspect : 1;
            var border = slot.BorderWidth;
            var maximum = Math.Max(1, Math.Min(480,
                Math.Min((captureWidth - border * 2) / widthFactor,
                    (captureHeight - border * 2) / heightFactor)));
            var size = Math.Min(slot.Size, (int)maximum);
            if (initial) size = Math.Min(size, Math.Max(1, (int)(captureHeight / 2d / heightFactor)));
            var width = Math.Max(1, (int)(size * widthFactor)) + border * 2;
            var height = Math.Max(1, (int)(size * heightFactor)) + border * 2;
            var position = slot.Position ?? new PixelPoint(captureWidth - 8 - width, captureHeight - 8 - height);
            var x = Math.Clamp(position.X, 0, Math.Max(0, captureWidth - width));
            var y = Math.Clamp(position.Y, 0, Math.Max(0, captureHeight - height));
            return new(x, y, x + width, y + height);
        }
    }
}
