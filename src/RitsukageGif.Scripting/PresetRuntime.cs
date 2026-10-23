namespace RitsukageGif.Scripting
{
    public sealed class PresetRuntime
    {
        private PresetRuntimeClock _clock = new();

        private PresetRuntimeSnapshot _snapshot = new(new Dictionary<string, object>(),
            new Dictionary<string, PresetLayerState>());

        public PresetRuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
        public double Now => _clock.Now;
        public void SetTime(double time) => _clock.SetTime(time);
        internal void ShareClock(PresetRuntime other) => _clock = other._clock;

        public void Publish(IReadOnlyDictionary<string, object> variables,
            IReadOnlyDictionary<string, PresetLayerState> layers) =>
            Volatile.Write(ref _snapshot,
                new PresetRuntimeSnapshot(new Dictionary<string, object>(variables),
                    new Dictionary<string, PresetLayerState>(layers)));
    }
}
