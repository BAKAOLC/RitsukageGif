using System.Globalization;
using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    public record ScreenshotSaveProfile
    {
        [JsonIgnore] protected string? PreferredExtension { get; init; }

        public string Directory { get; set; } = "";
        public ScreenshotAfterSaveAction AfterSave { get; set; }
        public bool Notify { get; set; } = true;
        public StaticImageFormat Format { get; set; } = StaticImageFormat.Png;
        public int Quality { get; set; } = 90;
        public ScreenshotOutputSize OutputSize { get; set; } = new();
        public string NameTemplate { get; set; } = "Screenshot-{timestamp}";
        public ScreenshotConflictPolicy ConflictPolicy { get; set; } = ScreenshotConflictPolicy.Rename;

        public ScreenshotSaveProfile Normalize()
        {
            return this with
            {
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                Directory = Directory?.Trim() ?? "",
                AfterSave = Enum.IsDefined(AfterSave) ? AfterSave : ScreenshotAfterSaveAction.None,
                Format = Enum.IsDefined(Format) ? Format : StaticImageFormat.Png,
                Quality = Math.Clamp(Quality, 0, 100),
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                OutputSize = (OutputSize ?? new()).Normalize(),
                NameTemplate = string.IsNullOrWhiteSpace(NameTemplate) ? "Screenshot-{timestamp}" : NameTemplate,
                ConflictPolicy = Enum.IsDefined(ConflictPolicy) ? ConflictPolicy : ScreenshotConflictPolicy.Rename,
            };
        }

        public StaticImageSaveOptions CreateOptions(StaticImageFormat? format = null)
        {
            return new()
            {
                Format = format ?? Format, Quality = Math.Clamp(Quality, 0, 100),
                Overwrite = ConflictPolicy == ScreenshotConflictPolicy.Overwrite,
            };
        }

        // ReSharper disable once UnusedMember.Global
        public string CreateFileName(DateTime timestamp, StaticImageFormat? format = null)
        {
            return CreateFileName(new ScreenshotNameContext(timestamp), format);
        }

        public string CreateFileName(ScreenshotNameContext context, StaticImageFormat? format = null)
        {
            var value = Normalize();
            var timestamp = context.Timestamp;
            var name = value.NameTemplate
                .Replace("{timestamp}", timestamp.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                .Replace("{date}", timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                .Replace("{time}", timestamp.ToString("HHmmss", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                .Replace("{title}", context.Title ?? "", StringComparison.Ordinal)
                .Replace("{process}", context.Process ?? "", StringComparison.Ordinal)
                .Replace("{width}", context.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{height}", context.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{uuid}", context.Id.ToString("N"), StringComparison.Ordinal);
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name
                .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character)
                .ToArray()).Trim(' ', '.');
            if (name.Length == 0) name = "Screenshot";
            if (name.Length > 160) name = name[..160];
            var extension = (format ?? value.Format) switch
            {
                StaticImageFormat.Png => "png",
                StaticImageFormat.Jpeg => "jpg",
                StaticImageFormat.Bmp => "bmp",
                StaticImageFormat.WebP => "webp",
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            return name + "." + extension;
        }

        internal static string? NormalizeExtension(string? extension)
        {
            var value = extension?.Trim().TrimStart('.').ToLowerInvariant();
            return value is "png" or "jpg" or "jpeg" or "bmp" or "webp" or "exr" ? "." + value : null;
        }

        internal string DialogExtension(OutputColorMode mode)
        {
            var preferred = NormalizeExtension(PreferredExtension);
            switch (mode)
            {
                case OutputColorMode.Hdr: return preferred == ".exr" ? ".exr" : ".png";
                case OutputColorMode.Both: return ".png";
                case OutputColorMode.Sdr: break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (preferred is not null and not ".exr") return preferred;
            return Format switch
            {
                StaticImageFormat.Png => ".png", StaticImageFormat.Jpeg => ".jpg",
                StaticImageFormat.Bmp => ".bmp", StaticImageFormat.WebP => ".webp",
                _ => ".png",
            };
        }

        public string ResolveDirectory(string fallback)
        {
            return Path.GetFullPath(string.IsNullOrWhiteSpace(Directory) ? fallback : Directory);
        }

        public string ResolvePath(string path)
        {
            if (ConflictPolicy != ScreenshotConflictPolicy.Rename || !Path.Exists(path)) return path;
            var directory = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (var index = 1;; index++)
            {
                var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
                if (!Path.Exists(candidate)) return candidate;
            }
        }
    }
}
