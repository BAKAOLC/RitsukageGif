using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static partial class GlobalActions
    {
        private static bool IsScreenshotGeometryArgument(string key)
        {
            return key is "x" or "y" or "width" or "height" or "ratio";
        }

        internal static ActionScreenshotGeometry ScreenshotGeometry(GlobalActionBinding action)
        {
            var x = ReadGeometryInteger(action.Arguments, "x", -99999);
            var y = ReadGeometryInteger(action.Arguments, "y", -99999);
            var width = ReadGeometryInteger(action.Arguments, "width", 1);
            var height = ReadGeometryInteger(action.Arguments, "height", 1);
            if (x.HasValue != y.HasValue || width.HasValue != height.HasValue)
                throw new ArgumentException(UiText.Get("GlobalActionInvalidGeometry"));
            double? ratio = null;
            if (action.Arguments.TryGetValue("ratio", out var value))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                    !double.IsFinite(parsed) || parsed <= 0)
                    throw new ArgumentException(UiText.Get("GlobalActionInvalidGeometry"));
                ratio = parsed;
            }

            return new(x, y, width, height, ratio);
        }

        private static int? ReadGeometryInteger(Dictionary<string, string> arguments, string key, int minimum)
        {
            if (!arguments.TryGetValue(key, out var value)) return null;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < minimum || parsed > 99999)
                throw new ArgumentException(UiText.Get("GlobalActionInvalidGeometry"));
            return parsed;
        }
    }
}
