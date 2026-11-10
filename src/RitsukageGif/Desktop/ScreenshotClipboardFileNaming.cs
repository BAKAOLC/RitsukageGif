namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotClipboardFileNaming(ScreenshotSaveProfile Profile, ScreenshotNameContext Context)
    {
        public string CreateName(string extension, string suffix)
        {
            var name = Profile.CreateFileName(Context);
            return Path.GetFileNameWithoutExtension(name) + suffix + extension;
        }
    }
}
