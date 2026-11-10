namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedViewportSample(double Red, double Green, double Blue, double Alpha)
    {
        internal PinnedViewportSample Add(PinnedViewportSample sample, double weight)
        {
            return new(Red + sample.Red * weight, Green + sample.Green * weight,
                Blue + sample.Blue * weight, Alpha + sample.Alpha * weight);
        }
    }
}
