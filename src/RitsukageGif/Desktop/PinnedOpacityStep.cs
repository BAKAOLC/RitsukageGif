namespace RitsukageGif.Desktop
{
    internal static class PinnedOpacityStep
    {
        internal static double Apply(double current, double step)
        {
            return step == 0 ? current : Math.Clamp(step * (Math.Floor(current / step + .5) + 1), 0, 1);
        }
    }
}
