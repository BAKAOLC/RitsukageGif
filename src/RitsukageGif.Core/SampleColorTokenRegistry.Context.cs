using System.Drawing;
using System.Globalization;

namespace RitsukageGif.Core
{
    public sealed partial class SampleColorTokenRegistry
    {
        private static readonly Dictionary<int, string> ColorNames = CreateColorNames();

        private void RegisterContext()
        {
            RegisterNumber("Dr", "Color", 'i',
                values => (values.Context.Red * 256 + values.Context.Green) * 256 + values.Context.Blue);
            RegisterNumber("Dv", "Color", 'i',
                values => (values.Context.Blue * 256 + values.Context.Green) * 256 + values.Context.Red);
            Register(new("Na", "Color", 's', "s", (values, _) =>
            {
                var rgb = (values.Context.Red * 256 + values.Context.Green) * 256 + values.Context.Blue;
                return ColorNames.TryGetValue(rgb, out var name)
                    ? name
                    : "#" + rgb.ToString("X6", CultureInfo.InvariantCulture);
            }));
            RegisterNumber("Mx", "Position", 'i', values => values.Context.Pointer.X, SampleColorTokenInputs.Pointer);
            RegisterNumber("My", "Position", 'i', values => values.Context.Pointer.Y, SampleColorTokenInputs.Pointer);
            RegisterNumber("Rx", "Selection", 'i', values => values.Context.Selection.X,
                SampleColorTokenInputs.Selection);
            RegisterNumber("Ry", "Selection", 'i', values => values.Context.Selection.Y,
                SampleColorTokenInputs.Selection);
            RegisterNumber("Rw", "Selection", 'i', values => values.Context.Selection.Width,
                SampleColorTokenInputs.Selection);
            RegisterNumber("Rh", "Selection", 'i', values => values.Context.Selection.Height,
                SampleColorTokenInputs.Selection);
        }

        private static Dictionary<int, string> CreateColorNames()
        {
            var names = new Dictionary<int, string>();
            foreach (var known in Enum.GetValues<KnownColor>()
                         .OrderBy(value => value.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                var color = Color.FromKnownColor(known);
                if (color.IsSystemColor || color.A != 255) continue;
                names.TryAdd((color.R * 256 + color.G) * 256 + color.B, known.ToString().ToLowerInvariant());
            }

            return names;
        }
    }
}
