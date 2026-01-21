using System;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider = 0)
        {
            return provider switch
            {
                0 => new BitbltScreenFrameProvider(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider)),
            };
        }
    }
}