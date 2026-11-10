using System.Text.Json.Serialization;

namespace RitsukageGif.Contracts
{
    /// <summary>Window decoration radii in logical source coordinates, independent of image resampling.</summary>
    public readonly record struct CaptureWindowShape(double RadiusX, double RadiusY)
    {
        [JsonIgnore]
        public bool IsValid => double.IsFinite(RadiusX) && double.IsFinite(RadiusY) &&
                               RadiusX >= 0 && RadiusY >= 0;

        [JsonIgnore] public bool IsEmpty => RadiusX == 0 && RadiusY == 0;
    }
}
