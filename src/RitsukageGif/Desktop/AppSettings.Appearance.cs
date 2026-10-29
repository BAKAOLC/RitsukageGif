namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public AppearancePreferences Appearance { get; set; } = new();
    }
}
