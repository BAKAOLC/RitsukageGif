using System.Text.Json.Serialization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public AnnotationNumberStyle NumberStyle { get; set; }
        public AnnotationNumberFormat NumberFormat { get; set; }
        public bool NumberAutomaticOutline { get; set; } = true;

        [JsonConverter(typeof(AnnotationColorJsonConverter))]
        public Color NumberOutlineColor { get; set; } = Colors.White;

        public bool NumberCallout { get; set; }
        public bool NumberCaptionGuide { get; set; }
        public double NumberCaptionScale { get; set; } = 1;
        public bool NumberCaptionOutline { get; set; }

        partial void NormalizeNumberProperties()
        {
            NumberStyle = Enum.IsDefined(NumberStyle) ? NumberStyle : AnnotationNumberStyle.FilledCircle;
            NumberFormat = Enum.IsDefined(NumberFormat) ? NumberFormat : AnnotationNumberFormat.Decimal;
            NumberCaptionScale = double.IsFinite(NumberCaptionScale) ? Math.Clamp(NumberCaptionScale, .25, 4) : 1;
        }
    }
}
