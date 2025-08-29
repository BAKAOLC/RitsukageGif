using System;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider = 0)
        {
            if (provider == 0)
                return new BitbltScreenFrameProvider();
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
    }
}