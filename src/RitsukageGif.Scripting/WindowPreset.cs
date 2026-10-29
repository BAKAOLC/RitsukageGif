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
        private static readonly ILogger Logger = LogHub.GetLogger("Scripting.PresetParser");

        public PixelRect Resolve(PixelRect window, PixelRect client,
            IReadOnlyDictionary<string, object>? variables = null)
        {
            return ResolveArea(Area, Absolute ? default : ClientArea ? client : window, Constants,
                variables ?? InitialValues);
        }

        public static PixelRect ResolveArea(IReadOnlyList<PresetExpression> area, PixelRect basis,
            IReadOnlyDictionary<string, double> constants, IReadOnlyDictionary<string, object>? variables = null)
        {
            var context = new PresetContext(new(0, "", basis), false, false, 0, 0, constants,
                Variables: variables);

            var result = new PixelRect(checked(basis.X + Measure(0)), checked(basis.Y + Measure(1)), Measure(2),
                Measure(3));
            if (result.IsEmpty) throw new ArgumentException(UiText.Get("InvalidRecording"));
            return result;

            int Measure(int index)
            {
                return checked((int)Math.Round(area[index].Number(context with
                {
                    PercentBasis = index % 2 == 0 ? basis.Width : basis.Height,
                })));
            }
        }

        public static WindowPreset ForWindow(string process, string title)
        {
            return new("", process, title, true,
                [new PresetLiteral(0d), new PresetLiteral(0d), new PresetPercent(100), new PresetPercent(100)], false,
                new(), new Dictionary<string, double>(), "", [], [], []);
        }

        public static bool Matches(string value, string pattern)
        {
            return FileSystemName.MatchesSimpleExpression(pattern, value);
        }

        public static IReadOnlyList<WindowPreset> Parse(string source, ScriptBindingRegistry? bindings = null)
        {
            try
            {
                var presets = new PresetParser(source, bindings).Parse();
                Logger.ZLogDebug($"Preset document parsed. CharacterCount={source.Length} PresetCount={presets.Count}");
                return presets;
            }
            catch (PresetSyntaxException error)
            {
                Logger.ZLogDebug($"Preset document parsing rejected. Line={error.Line} Column={error.Column}");
                throw;
            }
        }
    }
}
