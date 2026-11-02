namespace RitsukageGif.Desktop
{
    public sealed record HotkeyPolicyPreferences
    {
        public bool ShortcutsEnabled { get; set; } = true;
        public bool DisableInFullscreen { get; set; }
        public List<string> IgnoredPrograms { get; set; } = [];

        public HotkeyPolicyPreferences Normalize()
        {
            return this with { IgnoredPrograms = ProgramExclusionRules.Normalize(IgnoredPrograms) };
        }

        // ReSharper disable once OutParameterValueIsAlwaysDiscarded.Global
        public static bool TryNormalizeProgram(string? value, out string normalized)
        {
            return ProgramExclusionRules.TryNormalize(value, out normalized);
        }

        internal bool Ignores(string? path)
        {
            return ProgramExclusionRules.Matches(IgnoredPrograms, path);
        }
    }
}
