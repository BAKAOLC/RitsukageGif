using System.Text.Json;

namespace RitsukageGif.Desktop
{
    public sealed record AppSettings
    {
        public string Language { get; set; } = "system";
        public bool CheckForUpdates { get; set; } = true;
        public List<string> PresetFiles { get; set; } = [];
        public List<PresetHotkey> PresetHotkeys { get; set; } = [];
        public int Fps { get; set; } = 20;
        public int Divisor { get; set; } = 2;
        public OutputFormat Format { get; set; }
        public GifQuality Quality { get; set; } = GifQuality.Balanced;
        public bool UseFfmpeg { get; set; }
        public bool Cursor { get; set; }
        public CaptureSourceKind CaptureSource { get; set; }
        public bool Sound { get; set; }
        public string StartSound { get; set; } = "";
        public string StopSound { get; set; } = "";
        public string BackgroundImage { get; set; } = "";
        public int SnapRadius { get; set; } = 12;
        public int EdgeThreshold { get; set; } = 150;

        public string OutputDirectory { get; set; } =
            Path.Combine(DefaultPicturesDirectory(), "RitsukageGif");

        private static string DefaultPicturesDirectory()
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return string.IsNullOrEmpty(pictures)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures")
                : pictures;
        }

        public string RecordHotkey { get; set; } = "Ctrl+Shift+A";
        public string SelectHotkey { get; set; } = "Ctrl+Shift+S";

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RitsukageGif",
                "settings.json");

        public static AppSettings Load()
        {
            try
            {
                var settings = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppSettings) ??
                      new AppSettings()
                    : new AppSettings();
                settings.Fps = Math.Clamp(settings.Fps, 1, 100);
                settings.Divisor = Math.Clamp(settings.Divisor, 1, 100);
                settings.SnapRadius = Math.Clamp(settings.SnapRadius, 0, 50);
                settings.EdgeThreshold = Math.Clamp(settings.EdgeThreshold, 1, 765);
                settings.OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                    ? new AppSettings().OutputDirectory
                    : settings.OutputDirectory;
                if (!Enum.IsDefined(settings.Format)) settings.Format = OutputFormat.Gif;
                if (!Enum.IsDefined(settings.Quality)) settings.Quality = GifQuality.Balanced;
                if (!Enum.IsDefined(settings.CaptureSource)) settings.CaptureSource = CaptureSourceKind.Screen;
                return settings;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
            File.Move(temporary, FilePath, true);
        }

        public void Play(bool start)
        {
            var path = start ? StartSound : StopSound;
            if (Sound && File.Exists(path))
                PlatformShell.PlaySound(path);
        }
    }
}
