namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedColorMatrixState(bool GrayscaleEnabled, RgbColorMatrix? Matrix)
    {
        internal PinnedColorMatrixState ToggleGrayscale()
        {
            return GrayscaleEnabled ? default : new(true, Append(RgbColorMatrix.Grayscale));
        }

        internal PinnedColorMatrixState Invert()
        {
            return this with { Matrix = Append(RgbColorMatrix.Invert) };
        }

        internal PinnedColorMatrixState AdjustBrightness(float delta)
        {
            return this with { Matrix = Append(RgbColorMatrix.Brightness(delta)) };
        }

        private RgbColorMatrix Append(RgbColorMatrix next)
        {
            return Matrix is { } current ? current.Append(next) : next;
        }
    }
}
