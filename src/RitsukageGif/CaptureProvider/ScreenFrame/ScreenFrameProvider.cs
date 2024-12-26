namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider)
        {
            return provider switch
            {
                1 => new DXGIScreenFrameProvider(),
                _ => new BitbltScreenFrameProvider(),
            };
        }
    }
}