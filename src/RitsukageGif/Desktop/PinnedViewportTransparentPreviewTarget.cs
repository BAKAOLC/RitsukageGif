using System.Buffers.Binary;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportTransparentPreviewTarget : IPinnedViewportTarget
    {
        private readonly byte[] _output;
        private readonly int _width;
        private readonly Matrix _inverse;
        private readonly PinnedViewportCoverage _coverage;
        private readonly double _scaleX;
        private readonly double _scaleY;
        private readonly PinnedHdrPreviewBackground _background;

        internal PinnedViewportTransparentPreviewTarget(byte[] output, int width, Matrix sourceToTarget,
            PinnedHdrPreviewBackground background, PinnedViewportClip? clip = null)
        {
            _output = output;
            _width = width;
            _inverse = sourceToTarget.Invert();
            var sourceRegion = clip?.SourceDisplayRegion ?? new Rect(background.SourceBounds.X,
                background.SourceBounds.Y,
                background.SourceBounds.Width, background.SourceBounds.Height);
            _coverage = new(sourceRegion, clip?.TargetClip, sourceToTarget);
            _scaleX = Math.Sqrt(sourceToTarget.M11 * sourceToTarget.M11 + sourceToTarget.M12 * sourceToTarget.M12);
            _scaleY = Math.Sqrt(sourceToTarget.M21 * sourceToTarget.M21 + sourceToTarget.M22 * sourceToTarget.M22);
            _background = background;
        }

        public void Write(int pixel, PinnedViewportSample sample, double opacity)
        {
            var column = pixel % _width;
            var row = pixel / _width;
            var coverage = _coverage.At(column, row);
            if (coverage <= 0)
            {
                _output.AsSpan(pixel * 8, 8).Clear();
                return;
            }

            var x = column + .5;
            var y = row + .5;
            var sx = x * _inverse.M11 + y * _inverse.M21 + _inverse.M31;
            var sy = x * _inverse.M12 + y * _inverse.M22 + _inverse.M32;
            var alpha = sample.Alpha;
            var red = sample.Red;
            var green = sample.Green;
            var blue = sample.Blue;
            red *= coverage;
            green *= coverage;
            blue *= coverage;
            alpha *= coverage;
            if (coverage > 0)
            {
                var backgroundAlpha = _background.Mode switch
                {
                    PinnedTransparentLayerMode.AcceptingMouse => 1d / 255,
                    PinnedTransparentLayerMode.Checkerboard => 1,
                    _ => 0,
                };
                backgroundAlpha *= coverage;
                var gray = 0d;
                if (_background.Mode == PinnedTransparentLayerMode.Checkerboard)
                {
                    var diagonal = ((long)Math.Floor(sx * _scaleX / _background.CellPixels) +
                                    (long)Math.Floor(sy * _scaleY / _background.CellPixels)) % 2 == 0;
                    var srgb = (_background.Dark ? diagonal ? 50 : 71 : diagonal ? 192 : 255) / 255d;
                    gray = (srgb <= .04045 ? srgb / 12.92 : Math.Pow((srgb + .055) / 1.055, 2.4)) *
                           _background.WhiteLevel;
                }

                var weight = backgroundAlpha * (1 - alpha);
                red += gray * weight;
                green += gray * weight;
                blue += gray * weight;
                alpha += weight;
            }

            var bytes = _output.AsSpan(pixel * 8);
            BinaryPrimitives.WriteHalfLittleEndian(bytes, (Half)red);
            BinaryPrimitives.WriteHalfLittleEndian(bytes[2..], (Half)green);
            BinaryPrimitives.WriteHalfLittleEndian(bytes[4..], (Half)blue);
            BinaryPrimitives.WriteHalfLittleEndian(bytes[6..], (Half)alpha);
        }
    }
}
