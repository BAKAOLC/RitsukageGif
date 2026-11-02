namespace RitsukageGif.Core
{
    public sealed class SampleColorValues(SampleColorContext context)
    {
        private (double L, double A, double B)? _lab;
        private (double Red, double Green, double Blue)? _linear;
        private (double L, double A, double B)? _oklab;
        private (double X, double Y, double Z)? _xyz;
        public SampleColorContext Context { get; } = context;
        public double Red => Context.Red / 255d;
        public double Green => Context.Green / 255d;
        public double Blue => Context.Blue / 255d;
        public double Maximum => Math.Max(Red, Math.Max(Green, Blue));
        public double Minimum => Math.Min(Red, Math.Min(Green, Blue));
        public double Lightness => (Maximum + Minimum) / 2;
        public double Intensity => (Red + Green + Blue) / 3;
        public double HsvSaturation => Maximum == 0 ? 0 : (Maximum - Minimum) / Maximum;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        public double HslSaturation => Maximum == Minimum ? 0 : (Maximum - Minimum) / (1 - Math.Abs(2 * Lightness - 1));

        public double HsiSaturation => Intensity == 0 ? 0 : 1 - Minimum / Intensity;

        public double Hue
        {
            get
            {
                var delta = Maximum - Minimum;
                if (delta == 0) return 0;
                var sector = Red >= Green && Red >= Blue ? (Green - Blue) / delta % 6 :
                    Green >= Blue ? (Blue - Red) / delta + 2 : (Red - Green) / delta + 4;
                return (sector * 60 + 360) % 360;
            }
        }

        public (double X, double Y, double Z) Xyz
        {
            get
            {
                if (_xyz is { } result) return result;
                var (red, green, blue) = Linear;
                result = (.4124564 * red + .3575761 * green + .1804375 * blue,
                    .2126729 * red + .7151522 * green + .072175 * blue,
                    .0193339 * red + .119192 * green + .9503041 * blue);
                _xyz = result;
                return result;
            }
        }

        public (double L, double A, double B) Lab
        {
            get
            {
                if (_lab is { } result) return result;
                var xyz = Xyz;
                var x = LabCurve(xyz.X / .95047);
                var y = LabCurve(xyz.Y);
                var z = LabCurve(xyz.Z / 1.08883);
                result = (116 * y - 16, 500 * (x - y), 200 * (y - z));
                _lab = result;
                return result;
            }
        }

        public (double L, double A, double B) Oklab
        {
            get
            {
                if (_oklab is { } result) return result;
                var (red, green, blue) = Linear;
                var l = Math.Cbrt(.4122214708 * red + .5363325363 * green + .0514459929 * blue);
                var m = Math.Cbrt(.2119034982 * red + .6806995451 * green + .1073969566 * blue);
                var s = Math.Cbrt(.0883024619 * red + .2817188376 * green + .6299787005 * blue);
                result = (.2104542553 * l + .793617785 * m - .0040720468 * s,
                    1.9779984951 * l - 2.428592205 * m + .4505937099 * s,
                    .0259040371 * l + .7827717662 * m - .808675766 * s);
                _oklab = result;
                return result;
            }
        }

        private (double Red, double Green, double Blue) Linear => _linear ??=
            (Decode(Red), Decode(Green), Decode(Blue));

        private static double Decode(double value)
        {
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        private static double LabCurve(double value)
        {
            return value > 216d / 24389 ? Math.Cbrt(value) : value * (24389d / 27) / 116 + 16d / 116;
        }
    }
}
