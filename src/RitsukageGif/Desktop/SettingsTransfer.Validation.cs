using System.Text.Json;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateJson(JsonElement element, string parent)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in element.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.Null) throw Invalid(parent);
                    ValidateJson(value, parent);
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid(parent + "." + property.Name);
                var path = parent.Length == 0 ? property.Name : parent + "." + property.Name;
                var value = property.Value;
                ValidateOutputProperty(parent, property, path);
                ValidateDecorationProperty(parent, property, path);
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                    switch (property.Name)
                    {
                        case "CaptureColorMode":
                            if (!Enum.IsDefined((OutputColorMode)number)) throw Invalid(path);
                            break;
                        case "CaptureToneMap":
                            if (!Enum.IsDefined((HdrToneMapMode)number)) throw Invalid(path);
                            break;
                        case "Format" when parent.Length == 0:
                            if (!Enum.IsDefined((OutputFormat)number)) throw Invalid(path);
                            break;
                        case "Format" when parent == "ScreenshotSave":
                            if (!Enum.IsDefined((StaticImageFormat)number)) throw Invalid(path);
                            break;
                        case "FileFormat" when parent == "ScreenshotClipboard":
                            if (!Enum.IsDefined((ScreenshotClipboardFileFormat)number)) throw Invalid(path);
                            break;
                        case "Mode" when parent == "ScreenshotClipboard":
                            if (!Enum.IsDefined((ScreenshotClipboardMode)number)) throw Invalid(path);
                            break;
                        case "ConflictPolicy":
                            if (!Enum.IsDefined((ScreenshotConflictPolicy)number)) throw Invalid(path);
                            break;
                        case "Container":
                            if (!Enum.IsDefined((VideoContainer)number)) throw Invalid(path);
                            break;
                        case "Kind" when parent == "GlobalActions":
                            if (!Enum.IsDefined((GlobalActionKind)number)) throw Invalid(path);
                            break;
                        case "TraySingleClick" or "TrayDoubleClick" or "TrayMiddleClick" when parent == "System":
                            if (!Enum.IsDefined((SystemTrayAction)number)) throw Invalid(path);
                            break;
                        case "UpdateChannel" when parent == "System":
                            if (!Enum.IsDefined((UpdateChannel)number)) throw Invalid(path);
                            break;
                        case "Layout" when parent == "Recognition":
                            if (!Enum.IsDefined((RecognitionTextLayout)number)) throw Invalid(path);
                            break;
                        case "Punctuation" when parent == "Recognition":
                            if (!Enum.IsDefined((RecognitionPunctuation)number)) throw Invalid(path);
                            break;
                    }

                var invalidLanguage = parent == "Recognition" && property.Name == "LanguageTag" &&
                                      (value.ValueKind != JsonValueKind.String ||
                                       new RecognitionPreferences { LanguageTag = value.GetString()! }.Normalize()
                                           .LanguageTag !=
                                       value.GetString());
                if (invalidLanguage) throw Invalid(path);
                ValidateJson(value, path);
            }
        }

        private static void Validate(AppSettings settings)
        {
            if (settings.Fps is < 1 or > 100 || settings.Divisor is < 1 or > 100 ||
                settings.SnapRadius is < 0 or > 50 || settings.EdgeThreshold is < 1 or > 765 ||
                settings.SelectionSnappingMode is < 0 or > 3 || !Enum.IsDefined(settings.SelectionHints) ||
                !Enum.IsDefined(settings.Format) || !Enum.IsDefined(settings.CaptureColorMode) ||
                !Enum.IsDefined(settings.CaptureToneMap) || !Enum.IsDefined(settings.Quality) ||
                !Enum.IsDefined(settings.CaptureSource) || settings.Language is not ("system" or "en" or "zh-Hans") ||
                string.IsNullOrWhiteSpace(settings.OutputDirectory) ||
                settings.CleanupMaxAgeDays is <= 0 || settings.CleanupMaxFiles is <= 0 ||
                settings.CleanupMaxSizeMiB is <= 0)
                throw Invalid("settings");
            _ = Path.GetFullPath(settings.OutputDirectory);
            if (settings.VideoRecording.Fps is < 1 or > 100 || settings.VideoRecording.Divisor is < 1 or > 100 ||
                settings.VideoRecording.Quality is < 0 or > 51 || !Enum.IsDefined(settings.VideoRecording.Container))
                throw Invalid("VideoRecording");
            if (!Enum.IsDefined(settings.ScrollCapture.InitialAxis)) throw Invalid("ScrollCapture.InitialAxis");
            var capture = settings.CapturePreferences;
            if (!Enum.IsDefined(capture.DetectionMode) || !Enum.IsDefined(capture.CursorPolicy) ||
                !Enum.IsDefined(capture.AuxiliaryPolicy) || !Enum.IsDefined(capture.MagnifierColorFormat) ||
                capture.RegionHistoryCapacity is < 0 or > 1000 || capture.ScreenshotHistoryCapacity is < 0 or > 1000 ||
                capture.ScreenshotHistoryMaximumMiB is < 0 or > 512 || capture.ElementMargin is < -128 or > 128 ||
                capture.Magnification is < 2 or > 32 ||
                !Color.TryParse(capture.MagnifierGridColor, out _)) throw Invalid("CapturePreferences");
            var reserved = settings.PresetHotkeys.Select(binding => binding.Shortcut).ToArray();
            HotkeyService.Validate(reserved);
            var selectionIds = BuiltinShortcuts.Registrations().Select(definition => definition.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (settings.SelectionShortcuts.Keys.FirstOrDefault(id => !selectionIds.Contains(id)) is { } selectionId)
                throw Invalid(nameof(settings.SelectionShortcuts) + "." + selectionId);
            var mouseIds = MouseCommands.Catalog.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
            if (settings.MouseShortcuts.Keys.FirstOrDefault(id => !mouseIds.Contains(id)) is { } mouseId)
                throw Invalid(nameof(settings.MouseShortcuts) + "." + mouseId);
            var errors = SelectionCommands.Validate(settings.SelectionShortcuts)
                .Concat(MouseCommands.Validate(settings.MouseShortcuts))
                .Concat(GlobalActions.Validate(settings.GlobalActions, reserved, settings.GlobalMouseBindings))
                .Concat(GlobalMouseService.Validate(settings.GlobalMouseBindings, settings.GlobalActions)).ToArray();
            if (errors.Length != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
            if (settings.PresetFiles.Any(string.IsNullOrWhiteSpace)) throw Invalid("PresetFiles");
            using var document =
                JsonDocument.Parse(
                    JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
            ValidatePreferences(document.RootElement);
        }

        private static void ValidatePreferences(JsonElement root)
        {
            ValidateAnnotationPreferences(root);
            foreach (var preference in root.EnumerateObject())
            {
                if (preference.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var value in preference.Value.EnumerateObject())
                {
                    var path = preference.Name + "." + value.Name;
                    if (value.Value.ValueKind == JsonValueKind.Number)
                    {
                        var number = value.Value.GetDouble();
                        if (!double.IsFinite(number)) throw Invalid(path);
                        var valid = value.Name switch
                        {
                            "Quality" when preference.Name == "ScreenshotSave" => number is >= 0 and <= 100,
                            "Thickness" or "FontSize" => number is >= 1 and <= 1024,
                            "MosaicSize" => number is >= 2 and <= 256,
                            "Opacity" or "MouseThroughOpacity" => number is >= 0.15 and <= 1,
                            "ZoomStep" => number is >= 0.01 and <= 1,
                            "FineZoomStep" => number is >= 0.001 and <= 0.1,
                            "OpacityStep" => number is >= 0.01 and <= 0.5,
                            _ => true,
                        };
                        if (!valid) throw Invalid(path);
                    }

                    var invalid = value.Name switch
                    {
                        "Color" => value.Value.ValueKind != JsonValueKind.String ||
                                   !Color.TryParse(value.Value.GetString(), out _),
                        "LastTool" => !Enum.TryParse<ScreenshotTool>(value.Value.GetString(), out var tool) ||
                                      !Enum.IsDefined(tool),
                        "NameTemplate" => string.IsNullOrWhiteSpace(value.Value.GetString()),
                        _ => false,
                    };
                    if (invalid) throw Invalid(path);
                }
            }

            ValidateJson(root, "");
        }
    }
}
