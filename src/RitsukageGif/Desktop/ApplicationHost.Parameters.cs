namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private void RunPresetWithParameters(string file, string[] arguments)
        {
            string? name = null;
            var position = 0;
            if (arguments.Length > 0 && arguments[0] != "--arg") name = arguments[position++];
            var assignments = new List<string>();
            while (position < arguments.Length)
            {
                if (arguments[position++] != "--arg" || position >= arguments.Length)
                    throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                assignments.Add(arguments[position++]);
            }

            var parameters = ScriptParameters.Parse(assignments);
            var presets = PresetCatalog.Load([file]);
            ActivatePreset(name is null ? presets.Single() : presets.Single(p => p.Name == name),
                parameters: parameters);
        }
    }
}
