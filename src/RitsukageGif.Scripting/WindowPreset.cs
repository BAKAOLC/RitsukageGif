using System.IO.Enumeration;

namespace RitsukageGif.Scripting
{
    public sealed record WindowPreset(
        string Name,
        string Process,
        string Title,
        bool ClientArea,
        IReadOnlyList<PresetExpression> Area,
        bool Follow,
        PresetRecordingSettings Settings,
        IReadOnlyDictionary<string, double> Constants,
        string Initial,
        IReadOnlyList<PresetState> States,
        IReadOnlyList<PresetRule> Rules,
        IReadOnlyList<PresetOutput> Outputs,
        PresetLayout? Layout = null,
        CaptureSourceKind Source = CaptureSourceKind.Screen,
        PresetFallback? Missing = null,
        long FixedHandle = 0,
        bool Absolute = false,
        IReadOnlyDictionary<string, object>? InitialValues = null,
        IReadOnlyList<PresetFunction>? Functions = null,
        ScriptNode? Program = null,
        string? SourceFile = null)
    {
        public PixelRect Resolve(PixelRect window, PixelRect client,
            IReadOnlyDictionary<string, object>? variables = null) =>
            ResolveArea(Area, Absolute ? default : ClientArea ? client : window, Constants, variables ?? InitialValues);

        public static PixelRect ResolveArea(IReadOnlyList<PresetExpression> area, PixelRect basis,
            IReadOnlyDictionary<string, double> constants, IReadOnlyDictionary<string, object>? variables = null)
        {
            var context = new PresetContext(new PresetWindowState(0, "", basis), false, false, 0, 0, constants,
                Variables: variables);

            int Measure(int index) => checked((int)Math.Round(area[index].Number(context with
            {
                PercentBasis = index % 2 == 0 ? basis.Width : basis.Height
            })));

            var result = new PixelRect(checked(basis.X + Measure(0)), checked(basis.Y + Measure(1)), Measure(2),
                Measure(3));
            if (result.IsEmpty) throw new ArgumentException(UiText.Get("InvalidRecording"));
            return result;
        }

        public static WindowPreset ForWindow(string process, string title) => new("", process, title, true,
            [new PresetLiteral(0d), new PresetLiteral(0d), new PresetPercent(100), new PresetPercent(100)], false,
            new PresetRecordingSettings(), new Dictionary<string, double>(), "", [], [], []);

        public static bool Matches(string value, string pattern) =>
            FileSystemName.MatchesSimpleExpression(pattern, value);

        public static IReadOnlyList<WindowPreset> Parse(string source, ScriptBindingRegistry? bindings = null) =>
            new PresetParser(source, bindings).Parse();
    }
}
