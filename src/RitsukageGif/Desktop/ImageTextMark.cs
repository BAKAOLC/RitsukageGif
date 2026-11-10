using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record ImageTextMark
    {
        public required int Start { get; init; }
        public required int Length { get; init; }
        public required int Line { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Width { get; init; }
        public required double Height { get; init; }
        public required ImageTextMarkKind Kind { get; init; }

        [JsonConverter(typeof(AnnotationColorJsonConverter))]
        public required Color Color { get; init; }

        internal Rect Bounds => new(X, Y, Width, Height);

        internal bool IsValid => Start >= 0 && Length > 0 && Length <= int.MaxValue - Start && Line >= 0 &&
                                 Enum.IsDefined(Kind) && double.IsFinite(X) && double.IsFinite(Y) &&
                                 double.IsFinite(Width) && double.IsFinite(Height) && X >= 0 && Y >= 0 &&
                                 Width > 0 && Height > 0;
    }
}
