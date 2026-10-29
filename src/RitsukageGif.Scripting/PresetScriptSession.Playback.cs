namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        private PresetPlaybackState ChangePlayback(PresetMediaSource? media, PresetPlaybackState? previous,
            Dictionary<string, object?> options)
        {
            if (media is null) throw new ArgumentException(UiText.Get("ScriptTypeError"));
            if (media.Channel && (options.ContainsKey("position") || options.ContainsKey("speed") ||
                                  options.ContainsKey("loop")))
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            var now = Runtime.Now;
            var state = previous ?? new PresetPlaybackState(media.Position, media.Speed, false, media.Loop,
                media.HoldLast, now);
            var position = PlaybackPosition(state, now);
            if (options.GetValueOrDefault("position") is { } seek) position = ScriptEngine.Number(seek);
            var speed = options.GetValueOrDefault("speed") is { } rate ? ScriptEngine.Number(rate) : state.Speed;
            if (!double.IsFinite(position) || position < 0 || !double.IsFinite(speed) || speed < 0 ||
                speed > (media.Ffmpeg ? 16 : 100))
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            return new(position, speed,
                options.GetValueOrDefault("paused") is bool paused ? paused : state.Paused,
                options.GetValueOrDefault("loop") is bool loop ? loop : state.Loop,
                options.GetValueOrDefault("holdLast") is bool hold ? hold : state.HoldLast, now);
        }

        private static double PlaybackPosition(PresetPlaybackState state, double now)
        {
            return state.Position +
                   (state.Paused ? 0 : Math.Max(0, now - state.UpdatedAt) * state.Speed);
        }

        private Dictionary<string, object?>? PlaybackValue(PresetMediaSource? media, PresetPlaybackState? state)
        {
            if (media is null) return null;
            return new()
            {
                ["position"] = state is null ? media.Position : PlaybackPosition(state, Runtime.Now),
                ["speed"] = state?.Speed ?? media.Speed, ["paused"] = state?.Paused ?? false,
                ["loop"] = state?.Loop ?? media.Loop, ["holdLast"] = state?.HoldLast ?? media.HoldLast,
            };
        }

        private void InitializePlayback(double now)
        {
            Runtime.SetTime(now);
            foreach (var layer in preset.Layout?.Layers ?? [])
            {
                if (layer.Media is not { } media) continue;
                var state = _layers.GetValueOrDefault(layer.Name) ?? new PresetLayerState();
                var playback = state.Playback is { } existing
                    ? existing with { UpdatedAt = now }
                    : new(media.Position, media.Speed, false, media.Loop, media.HoldLast, now);
                _layers[layer.Name] = state with { Playback = playback };
            }

            Runtime.Publish(_values, _layers);
        }
    }
}
