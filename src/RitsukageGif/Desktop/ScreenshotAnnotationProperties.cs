using System.Text.Json.Serialization;
using Avalonia.Media;
using RitsukageGif.SettingsGeneration;

namespace RitsukageGif.Desktop
{
    [GenerateSettingsMerge(typeof(AppSettings))]
    public sealed partial record ScreenshotAnnotationProperties
    {
        public bool Filled { get; set; }
        public AnnotationGeometryShape GeometryShape { get; set; }
        public AnnotationStrokeStyle StrokeStyle { get; set; } = AnnotationStrokeStyle.Solid;
        public double CornerRadius { get; set; }
        public bool Curved { get; set; }
        public AnnotationArrowShape ArrowShape { get; set; } = AnnotationArrowShape.Straight;
        public AnnotationEndpointStyle StartEndpoint { get; set; }
        public AnnotationEndpointStyle EndEndpoint { get; set; }
        public bool SpotlightEllipse { get; set; }
        public bool SpotlightStroke { get; set; }
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
        public int Number { get; set; } = 1;
        public bool Tiled { get; set; }
        public bool NineGrid { get; set; }
        public int Anchor { get; set; } = 4;
        public double ImageWidth { get; set; } = 256;
        public int BlurRadius { get; set; } = 8;
        public string FontFamily { get; set; } = "";

        public ScreenshotAnnotationProperties Normalize()
        {
            var value = this with
            {
                GeometryShape = Enum.IsDefined(GeometryShape) ? GeometryShape : AnnotationGeometryShape.Rectangle,
                ArrowShape = Enum.IsDefined(ArrowShape) ? ArrowShape : AnnotationArrowShape.Straight,
                StrokeStyle = Enum.IsDefined(StrokeStyle) ? StrokeStyle : AnnotationStrokeStyle.Solid,
                CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 1024) : 0,
                StartEndpoint = Enum.IsDefined(StartEndpoint) ? StartEndpoint : AnnotationEndpointStyle.None,
                EndEndpoint = Enum.IsDefined(EndEndpoint) ? EndEndpoint : AnnotationEndpointStyle.None,
                TextPadding = double.IsFinite(TextPadding) ? Math.Clamp(TextPadding, 0, 128) : 4,
                TextOutlineWidth = double.IsFinite(TextOutlineWidth) ? Math.Clamp(TextOutlineWidth, 0, 16) : 1,
                WrapWidth = double.IsFinite(WrapWidth) ? Math.Clamp(WrapWidth, 0, 8192) : 0,
                Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .01, 1) : .5,
                Number = Math.Clamp(Number, 0, 999999),
                Anchor = Math.Clamp(Anchor, 0, 8),
                ImageWidth = double.IsFinite(ImageWidth) ? Math.Clamp(ImageWidth, 1, 8192) : 256,
                BlurRadius = Math.Clamp(BlurRadius, 1, 256),
                FontFamily = NormalizeFontFamily(FontFamily),
            };
            value.NormalizeMarkerProperties();
            value.NormalizeNumberProperties();
            value.NormalizeMagnifierProperties();
            value.NormalizePixelToolProperties();
            return value;
        }

        partial void NormalizeMarkerProperties();
        partial void NormalizeNumberProperties();
        partial void NormalizeMagnifierProperties();
        partial void NormalizePixelToolProperties();

        private static string NormalizeFontFamily(string? value)
        {
            return value?.Trim() is { Length: <= 256 } family ? family : "";
        }
    }
}
