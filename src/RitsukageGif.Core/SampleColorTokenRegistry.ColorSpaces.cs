namespace RitsukageGif.Core
{
    public sealed partial class SampleColorTokenRegistry
    {
        private void RegisterColorSpaces()
        {
            RegisterNumber("Hu", "Hue", 'i', values => values.Hue);
            Register(new("Hn", "Hue", 's', "s", (values, _) =>
            {
                var hue = values.Hue;
                var index = Math.Min(5, (int)(hue / 60));
                return "RYGCBM"[index] + Number((hue - index * 60) / .6, 'i');
            }));
            RegisterNumber("Si", "Hsi", 'p', values => values.HsiSaturation);
            RegisterNumber("In", "Hsi", 'p', values => values.Intensity);
            RegisterNumber("Sl", "Hsl", 'p', values => values.HslSaturation);
            RegisterNumber("Ll", "Hsl", 'p', values => values.Lightness);
            RegisterNumber("Li", "Hsl", 'p', values => values.Lightness);
            RegisterNumber("Sb", "Hsv", 'p', values => values.HsvSaturation);
            RegisterNumber("Va", "Hsv", 'p', values => values.Maximum);
            RegisterNumber("Br", "Hsv", 'p', values => values.Maximum);
            RegisterNumber("Wh", "Hwb", 'p', values => values.Minimum);
            RegisterNumber("Bn", "Hwb", 'p', values => 1 - values.Maximum);
            RegisterNumber("Lc", "Lab", 'f', values => values.Lab.L);
            RegisterNumber("Ca", "Lab", 'f', values => values.Lab.A);
            RegisterNumber("Cb", "Lab", 'f', values => values.Lab.B);
            RegisterNumber("Lo", "Oklab", 'f', values => values.Oklab.L);
            RegisterNumber("Oa", "Oklab", 'f', values => values.Oklab.A);
            RegisterNumber("Ob", "Oklab", 'f', values => values.Oklab.B);
            RegisterNumber("Oc", "Oklch", 'f',
                values => Math.Sqrt(values.Oklab.A * values.Oklab.A + values.Oklab.B * values.Oklab.B));
            RegisterNumber("Oh", "Oklch", 'i',
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                values => values.Maximum == values.Minimum
                    ? 0
                    : (Math.Atan2(values.Oklab.B, values.Oklab.A) * 180 / Math.PI + 360) % 360);
            RegisterNumber("Xv", "Xyz", 'f', values => values.Xyz.X);
            RegisterNumber("Yv", "Xyz", 'f', values => values.Xyz.Y);
            RegisterNumber("Zv", "Xyz", 'f', values => values.Xyz.Z);
        }
    }
}
