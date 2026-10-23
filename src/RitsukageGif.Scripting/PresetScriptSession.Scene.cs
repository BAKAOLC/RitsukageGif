namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        internal object? SetLayerBound(string name, Dictionary<string, object?> options)
        {
            var definition = Layer(name);
            var state = _layers.GetValueOrDefault(name) ?? new PresetLayerState();
            if (options.GetValueOrDefault("playback") is Dictionary<string, object?> playback)
                state = state with { Playback = ChangePlayback(definition.Media, state.Playback, playback) };
            if (options.TryGetValue("effects", out var effects))
                state = state with { Effects = PixelEffectsBinding.Read(effects) };
            if (options.GetValueOrDefault("placement") is Dictionary<string, object?> placement)
                state = state with { Placement = Rectangle(placement), Motion = null };
            if (options.GetValueOrDefault("crop") is Dictionary<string, object?> crop)
                state = state with { Crop = Rectangle(crop) };
            if (options.GetValueOrDefault("visible") is bool visible)
                state = state with { Visible = visible };
            if (options.GetValueOrDefault("opacity") is double opacity)
            {
                if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
                    throw new ArgumentException(UiText.Format("DslRange", 0, 1));
                state = state with { Opacity = opacity };
            }

            // Publish crop and placement together: a capture frame never observes half an update.
            _layers[name] = state;
            Runtime.Publish(_values, _layers);
            foreach (var route in _inheritedOutputScenes) route.SetLayerBound(name, options);
            return null;
        }

        private static PixelRect Rectangle(Dictionary<string, object?> fields)
        {
            int Component(string name) => checked((int)Math.Round(ScriptEngine.Number(fields[name])));
            var rectangle = new PixelRect(Component("x"), Component("y"), Component("width"), Component("height"));
            if (rectangle.IsEmpty || rectangle.Width > 16384 || rectangle.Height > 16384 ||
                Math.Abs((long)rectangle.X) > 131072 || Math.Abs((long)rectangle.Y) > 131072)
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            return rectangle;
        }
    }
}
