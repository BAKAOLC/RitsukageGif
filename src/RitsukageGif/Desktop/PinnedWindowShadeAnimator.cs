using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedWindowShadeAnimator
    {
        private const int FrameCount = 30;
        private int Step { get; set; }
        internal bool Complete => Step == FrameCount;

        internal bool Advance()
        {
            if (!Complete) Step++;
            return Complete;
        }

        internal void Reset()
        {
            Step = 0;
        }

        internal Color? GetColor(uint target, bool enabled)
        {
            var color = Color.FromUInt32(target);
            if (Complete) return enabled ? color : null;
            var amount = Bounce((float)Step / FrameCount);
            var alpha = enabled ? byte.MaxValue : (byte)Math.Round(255 * (1 - amount), MidpointRounding.AwayFromZero);
            return new(alpha, Channel(color.R, amount), Channel(color.G, amount), Channel(color.B, amount));
        }

        private static float Bounce(float progress)
        {
            var time = progress;
            var height = 0f;
            switch ((double)time)
            {
                case >= 0.9090909090909091:
                    time -= 0.95454544f;
                    height = 0.984375f;
                    break;
                case >= 0.72727275f:
                    time -= 0.8181818f;
                    height = 0.9375f;
                    break;
                case >= 0.36363637f:
                    time -= 0.54545456f;
                    height = 0.75f;
                    break;
            }

            return time * 7.5625f * time + height;
        }

        private static byte Channel(byte target, float amount)
        {
            return (byte)Math.Round(255 + (target - 255) * amount, MidpointRounding.AwayFromZero);
        }
    }
}
