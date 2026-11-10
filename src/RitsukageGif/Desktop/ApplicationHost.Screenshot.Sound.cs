namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private ScreenshotSoundPlayer? _screenshotSound;

        private void PlayScreenshotSound(ScreenshotSoundTiming timing)
        {
            if ((Settings.CapturePreferences.SoundTiming & timing) != 0)
                (_screenshotSound ??= new()).Play();
        }
    }
}
