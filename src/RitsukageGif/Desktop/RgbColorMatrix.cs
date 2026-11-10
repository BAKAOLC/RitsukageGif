using System.Numerics;

namespace RitsukageGif.Desktop
{
    internal readonly record struct RgbColorMatrix(
        float RedRed,
        float RedGreen,
        float RedBlue,
        float RedOffset,
        float GreenRed,
        float GreenGreen,
        float GreenBlue,
        float GreenOffset,
        float BlueRed,
        float BlueGreen,
        float BlueBlue,
        float BlueOffset)
    {
        internal bool IsFinite => float.IsFinite(RedRed) && float.IsFinite(RedGreen) && float.IsFinite(RedBlue) &&
                                  float.IsFinite(RedOffset) && float.IsFinite(GreenRed) && float.IsFinite(GreenGreen) &&
                                  float.IsFinite(GreenBlue) && float.IsFinite(GreenOffset) && float.IsFinite(BlueRed) &&
                                  float.IsFinite(BlueGreen) && float.IsFinite(BlueBlue) && float.IsFinite(BlueOffset);

        internal static RgbColorMatrix Invert => new(-1, 0, 0, 255, 0, -1, 0, 255, 0, 0, -1, 255);

        internal static RgbColorMatrix Grayscale => new(
            .2126f, .7152f, .0722f, 0, .2126f, .7152f, .0722f, 0, .2126f, .7152f, .0722f, 0);

        internal static RgbColorMatrix Brightness(float delta)
        {
            var offset = delta * 255f;
            return new(1, 0, 0, offset, 0, 1, 0, offset, 0, 0, 1, offset);
        }

        internal Vector3 Transform(Vector3 value, float offsetScale = 1)
        {
            return new(
                RedRed * value.X + RedGreen * value.Y + RedBlue * value.Z + RedOffset * offsetScale,
                GreenRed * value.X + GreenGreen * value.Y + GreenBlue * value.Z + GreenOffset * offsetScale,
                BlueRed * value.X + BlueGreen * value.Y + BlueBlue * value.Z + BlueOffset * offsetScale);
        }

        internal RgbColorMatrix Append(RgbColorMatrix next)
        {
            return new(
                next.RedRed * RedRed + next.RedGreen * GreenRed + next.RedBlue * BlueRed,
                next.RedRed * RedGreen + next.RedGreen * GreenGreen + next.RedBlue * BlueGreen,
                next.RedRed * RedBlue + next.RedGreen * GreenBlue + next.RedBlue * BlueBlue,
                next.RedRed * RedOffset + next.RedGreen * GreenOffset + next.RedBlue * BlueOffset + next.RedOffset,
                next.GreenRed * RedRed + next.GreenGreen * GreenRed + next.GreenBlue * BlueRed,
                next.GreenRed * RedGreen + next.GreenGreen * GreenGreen + next.GreenBlue * BlueGreen,
                next.GreenRed * RedBlue + next.GreenGreen * GreenBlue + next.GreenBlue * BlueBlue,
                next.GreenRed * RedOffset + next.GreenGreen * GreenOffset + next.GreenBlue * BlueOffset +
                next.GreenOffset,
                next.BlueRed * RedRed + next.BlueGreen * GreenRed + next.BlueBlue * BlueRed,
                next.BlueRed * RedGreen + next.BlueGreen * GreenGreen + next.BlueBlue * BlueGreen,
                next.BlueRed * RedBlue + next.BlueGreen * GreenBlue + next.BlueBlue * BlueBlue,
                next.BlueRed * RedOffset + next.BlueGreen * GreenOffset + next.BlueBlue * BlueOffset + next.BlueOffset);
        }
    }
}
