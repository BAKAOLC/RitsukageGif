using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public string Language { get; set; } = "system";
        public bool CleanupEnabled { get; set; }
        public int? CleanupMaxAgeDays { get; set; }
        public int? CleanupMaxFiles { get; set; }
        public int? CleanupMaxSizeMiB { get; set; }
        public bool CheckForUpdates { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool MinimizeToTray { get; set; }
        public bool RestorePinnedImages { get; set; } = true;
        public ScreenshotClipboardPreferences ScreenshotClipboard { get; set; } = new();
        public ScreenshotSavePreferences ScreenshotSave { get; set; } = new();
        public AnnotationPreferences Annotation { get; set; } = new();
        public PinnedImagePreferences PinnedImages { get; set; } = new();
        public List<string> PresetFiles { get; set; } = [];
        public List<PresetHotkey> PresetHotkeys { get; set; } = [];
        public ScrollCapturePreferences ScrollCapture { get; set; } = new();
        public CapturePreferences CapturePreferences { get; set; } = new();
        public HotkeyPolicyPreferences HotkeyPolicy { get; set; } = new();
        public List<GlobalActionBinding> GlobalActions { get; set; } = CreateDefaultActions();
        public List<GlobalMouseBinding> GlobalMouseBindings { get; set; } = [];
        public VideoRecordingPreferences VideoRecording { get; set; } = new();
        public int Fps { get; set; } = 20;
        public int Divisor { get; set; } = 2;
        public OutputFormat Format { get; set; }
        public OutputColorMode CaptureColorMode { get; set; }
        public HdrToneMapMode CaptureToneMap { get; set; }
        public GifQuality Quality { get; set; } = GifQuality.Balanced;
        public bool UseFfmpeg { get; set; }
        public bool Cursor { get; set; }
        public CaptureSourceKind CaptureSource { get; set; }
        public bool Sound { get; set; }
        public string StartSound { get; set; } = "";
        public string StopSound { get; set; } = "";
        public string BackgroundImage { get; set; } = "";
        public bool ShowBackground { get; set; } = true;
        public int SnapRadius { get; set; } = 12;
        public int EdgeThreshold { get; set; } = 150;
        public bool SelectionDetectControls { get; set; }
        public int SelectionSnappingMode { get; set; } = 3;
        public SelectionHintMode SelectionHints { get; set; }

        public string OutputDirectory { get; set; } =
            Path.Combine(DefaultPicturesDirectory(), "RitsukageGif");

        public Dictionary<string, string[]> SelectionShortcuts { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string[]> MouseShortcuts { get; set; } = new(StringComparer.Ordinal);

        private static string FilePath => Path.Combine(ApplicationStorage.DataDirectory, "settings.json");

        private static string DefaultPicturesDirectory()
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return string.IsNullOrEmpty(pictures)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures")
                : pictures;
        }

        public static AppSettings Load()
        {
            try
            {
                var settings = File.Exists(FilePath)
                    ? Deserialize(File.ReadAllText(FilePath))
                    : new();
                settings.Fps = Math.Clamp(settings.Fps, 1, 100);
                settings.Divisor = Math.Clamp(settings.Divisor, 1, 100);
                settings.SnapRadius = Math.Clamp(settings.SnapRadius, 0, 50);
                settings.EdgeThreshold = Math.Clamp(settings.EdgeThreshold, 1, 765);
                settings.SelectionSnappingMode = Math.Clamp(settings.SelectionSnappingMode, 0, 3);
                if (!Enum.IsDefined(settings.SelectionHints)) settings.SelectionHints = SelectionHintMode.Full;
                settings.OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                    ? new AppSettings().OutputDirectory
                    : settings.OutputDirectory;
                if (!Enum.IsDefined(settings.Format)) settings.Format = OutputFormat.Gif;
                if (!Enum.IsDefined(settings.CaptureColorMode)) settings.CaptureColorMode = OutputColorMode.Sdr;
                if (!Enum.IsDefined(settings.CaptureToneMap))
                    settings.CaptureToneMap = HdrToneMapMode.HighlightCompression;
                if (!Enum.IsDefined(settings.Quality)) settings.Quality = GifQuality.Balanced;
                if (!Enum.IsDefined(settings.CaptureSource)) settings.CaptureSource = CaptureSourceKind.Screen;
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.ScreenshotClipboard = (settings.ScreenshotClipboard ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.ScreenshotSave = (settings.ScreenshotSave ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.Annotation = (settings.Annotation ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.PinnedImages = (settings.PinnedImages ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.System = (settings.System ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.Recognition = (settings.Recognition ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.HotkeyPolicy = (settings.HotkeyPolicy ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.CapturePreferences = (settings.CapturePreferences ?? new()).Normalize();
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                settings.ScrollCapture = (settings.ScrollCapture ?? new()).Normalize();
                settings.SelectionShortcuts = NormalizeShortcuts(settings.SelectionShortcuts);
                settings.MouseShortcuts = NormalizeMouseShortcuts(settings.MouseShortcuts);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                settings.GlobalMouseBindings.RemoveAll(binding => binding is null);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                settings.GlobalActions.RemoveAll(action => action is null);
                foreach (var action in settings.GlobalActions)
                    action.Arguments = new(action.Arguments, StringComparer.Ordinal);
                settings.VideoRecording = settings.VideoRecording with
                {
                    Container = Enum.IsDefined(settings.VideoRecording.Container)
                        ? settings.VideoRecording.Container
                        : VideoContainer.Mp4,
                    Fps = Math.Clamp(settings.VideoRecording.Fps, 1, 100),
                    Divisor = Math.Clamp(settings.VideoRecording.Divisor, 1, 100),
                    Quality = Math.Clamp(settings.VideoRecording.Quality, 0, 51),
                };
                return settings;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new();
            }
        }

        private static Dictionary<string, string[]> NormalizeShortcuts(Dictionary<string, string[]>? values)
        {
            return values is null
                ? new(StringComparer.Ordinal)
                : values.ToDictionary(pair => pair.Key,
                    pair => pair.Value is null
                        ? []
                        : pair.Value.Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    StringComparer.Ordinal);
        }

        private static Dictionary<string, string[]> NormalizeMouseShortcuts(Dictionary<string, string[]>? values)
        {
            var previous = NormalizeShortcuts(values);
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var definition in MouseCommands.Catalog)
            {
                if (!previous.TryGetValue(definition.Id, out var gestures)) continue;
                result[definition.Id] = [.. gestures.Take(1)];
            }

            return result;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
            File.Move(temporary, FilePath, true);
        }

        public void Play(bool start, Action<string> reportError)
        {
            var path = start ? StartSound : StopSound;
            if (Sound && !string.IsNullOrWhiteSpace(path))
                _ = PlayAsync(path, reportError);
        }

        private static async Task PlayAsync(string path, Action<string> reportError)
        {
            try
            {
                await PlatformShell.PlaySoundAsync(path);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Audio playback failed: {0}", ex);
                reportError(string.Format(UiText.Get("SoundPlaybackFailed"), ex.Message));
            }
        }
    }
}
