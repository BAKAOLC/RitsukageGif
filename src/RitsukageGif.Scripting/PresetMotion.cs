using System.Diagnostics;

namespace RitsukageGif.Scripting
{
    public sealed record PresetMotion(PixelRect From, PixelRect To, double Started, double Duration, string Easing)
    {
        public static double Now =>
            (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        public PixelRect At(double time)
        {
            var progress = Math.Clamp((time - Started) / Duration, 0, 1);
            var t = Easing switch
            {
                "ease-in" => progress * progress, "ease-out" => 1 - (1 - progress) * (1 - progress),
                "ease-in-out" => progress * progress * (3 - 2 * progress), _ => progress,
            };

            return new(Mix(From.X, To.X), Mix(From.Y, To.Y), Math.Max(1, Mix(From.Width, To.Width)),
                Math.Max(1, Mix(From.Height, To.Height)));

            int Mix(int a, int b)
            {
                return (int)Math.Round(a + (b - a) * t);
            }
        }
    }
}
