using System.Text.Json.Serialization;

namespace RitsukageGif.Contracts
{
    public sealed record CaptureDecoration(double CornerRadius, CaptureDecorationKind Kind, int Strength, uint Color)
    {
        public static CaptureDecoration None { get; } = new(0, CaptureDecorationKind.None, 0, 0);
        [JsonIgnore] public bool IsEmpty => CornerRadius == 0 && Kind == CaptureDecorationKind.None;

        public void Validate()
        {
            if (!double.IsFinite(CornerRadius) || CornerRadius < 0 || !Enum.IsDefined(Kind) || Strength < 0)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
        }
    }
}
