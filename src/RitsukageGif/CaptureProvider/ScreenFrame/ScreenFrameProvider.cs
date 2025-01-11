namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider)
        {
            return provider switch
            {
                1 => new DxgiScreenFrameProvider(),
                _ => new BitbltScreenFrameProvider(),
            };
        }
    }
}