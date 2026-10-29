using System.Text.Json.Serialization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotAnnotationProperties
    {
        public bool Filled { get; set; }
        public bool Dashed { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool StrikeThrough { get; set; }

        [JsonConverter(typeof(AnnotationColorJsonConverter))]
        public Color TextBackground { get; set; } = Colors.Transparent;

        [JsonConverter(typeof(AnnotationColorJsonConverter))]
        public Color TextOutline { get; set; } = Colors.Transparent;

        public double TextOutlineWidth { get; set; } = 1;
        public double WrapWidth { get; set; }
        public double TextPadding { get; set; } = 4;
        public double Opacity { get; set; } = .5;
        public double Magnification { get; set; } = 2;
        public int Number { get; set; } = 1;
        public bool Tiled { get; set; }
        public bool NineGrid { get; set; }
        public int Anchor { get; set; } = 4;
        public double ImageWidth { get; set; } = 256;
        public int BlurRadius { get; set; } = 8;
        public string FontFamily { get; set; } = "";

        public ScreenshotAnnotationProperties Normalize()
        {
            return this with
            {
                TextPadding = double.IsFinite(TextPadding) ? Math.Clamp(TextPadding, 0, 128) : 4,
                TextOutlineWidth = double.IsFinite(TextOutlineWidth) ? Math.Clamp(TextOutlineWidth, 0, 16) : 1,
                WrapWidth = double.IsFinite(WrapWidth) ? Math.Clamp(WrapWidth, 0, 8192) : 0,
                Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .01, 1) : .5,
                Magnification = double.IsFinite(Magnification) ? Math.Clamp(Magnification, 1.1, 16) : 2,
                Number = Math.Clamp(Number, 0, 999999),
                Anchor = Math.Clamp(Anchor, 0, 8),
                ImageWidth = double.IsFinite(ImageWidth) ? Math.Clamp(ImageWidth, 1, 8192) : 256,
                BlurRadius = Math.Clamp(BlurRadius, 1, 256),
                FontFamily = NormalizeFontFamily(FontFamily),
            };
        }

        private static string NormalizeFontFamily(string? value)
        {
            return value?.Trim() is { Length: <= 256 } family ? family : "";
        }
    }
}
