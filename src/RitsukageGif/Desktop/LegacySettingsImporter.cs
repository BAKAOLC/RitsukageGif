using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RitsukageGif.Desktop
{
    internal static class LegacySettingsImporter
    {
        internal static AppSettings? FindPrevious()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var candidates = new List<string>();
                foreach (var directory in Directory.EnumerateDirectories(root, "RitsukageGif*"))
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                    };
                    candidates.AddRange(Directory.EnumerateFiles(directory, "user.config", options));
                }

                foreach (var path in candidates.OrderByDescending(File.GetLastWriteTimeUtc))
                    try
                    {
                        return Import(path, new AppSettings());
                    }
                    catch (Exception error) when (error is IOException or XmlException or ArgumentException
                                                      or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Trace.TraceWarning("Legacy settings could not be read: {0}", error.Message);
                    }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Legacy settings search failed: {0}", error.Message);
            }

            return null;
        }

        internal static AppSettings Import(string path, AppSettings current)
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024,
            });
            var document = XDocument.Load(reader);
            var section = document.Root?.Element("userSettings")?.Element("RitsukageGif.Settings");
            if (section is null) throw new ArgumentException(UiText.Get("LegacySettingsInvalid"));
            var values = section.Elements("setting")
                .Where(element => element.Attribute("name") is not null)
                .GroupBy(element => element.Attribute("name")!.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Element("value")?.Value ?? "",
                    StringComparer.Ordinal);
            var result = current with
            {
                PresetFiles = [.. current.PresetFiles], PresetHotkeys = [.. current.PresetHotkeys]
            };
            if (TryInt("RecordFrameFps", out var fps)) result.Fps = Math.Clamp(fps, 1, 100);
            if (TryInt("RecordFrameScale", out var scale)) result.Divisor = Math.Clamp(scale, 1, 100);
            if (TryInt("OutputFormat", out var format) && Enum.IsDefined((OutputFormat)format))
                result.Format = (OutputFormat)format;
            if (TryBool("RecordCursor", out var cursor)) result.Cursor = cursor;
            if (TryBool("CheckForUpdates", out var updates)) result.CheckForUpdates = updates;
            if (TryBool("UseFfmpegEncoder", out var ffmpeg)) result.UseFfmpeg = ffmpeg;
            if (values.TryGetValue("BackgroundImage", out var background))
            {
                result.ShowBackground = !string.IsNullOrWhiteSpace(background);
                result.BackgroundImage = background.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : LocalPath(background);
            }

            if (values.TryGetValue("StartRecordingSoundFile", out var start)) result.StartSound = SoundPath(start);
            if (values.TryGetValue("StopRecordingSoundFile", out var stop)) result.StopSound = SoundPath(stop);
            if (values.ContainsKey("StartRecordingSoundFile") || values.ContainsKey("StopRecordingSoundFile"))
                result.Sound = result.StartSound.Length > 0 || result.StopSound.Length > 0;
            if (values.TryGetValue("HotKeyRecordGif", out var record))
            {
                HotkeyService.Parse(record);
                result.RecordHotkey = record;
            }

            if (values.TryGetValue("HotKeySelectRegion", out var select))
            {
                HotkeyService.Parse(select);
                result.SelectHotkey = select;
            }

            HotkeyService.Validate([
                result.RecordHotkey, result.SelectHotkey, .. result.PresetHotkeys.Select(key => key.Shortcut)
            ]);
            return result;

            bool TryInt(string key, out int value) => int.TryParse(values.GetValueOrDefault(key), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);

            bool TryBool(string key, out bool value) => bool.TryParse(values.GetValueOrDefault(key), out value);
        }

        private static string SoundPath(string path) =>
            path.StartsWith("embedded:", StringComparison.OrdinalIgnoreCase) ? "" : LocalPath(path);

        private static string LocalPath(string path) =>
            Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : path;
    }
}
