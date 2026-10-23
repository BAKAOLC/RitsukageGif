using System.Globalization;

namespace RitsukageGif.Scripting
{
    public sealed class ScriptParameters
    {
        private readonly Lock _gate = new();
        private Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        private long _revision;
        private TaskCompletionSource<long> _changed = NewSignal();

        private static TaskCompletionSource<long> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Dictionary<string, object?> State()
        {
            lock (_gate)
                return new Dictionary<string, object?>
                {
                    ["revision"] = (double)_revision,
                    ["values"] = new Dictionary<string, object?>(_values, StringComparer.Ordinal)
                };
        }

        public async ValueTask<long> WaitChangedAsync(double revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(revision) || revision < 0 || !revision.Equals(Math.Truncate(revision)))
                throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
            Task<long> pending;
            lock (_gate)
            {
                if (!revision.Equals(_revision)) return _revision;
                pending = _changed.Task;
            }

            return await pending.WaitAsync(token);
        }

        public long Revision
        {
            get
            {
                lock (_gate) return _revision;
            }
        }

        public object? Get(string name, object? fallback = null)
        {
            lock (_gate) return _values.GetValueOrDefault(name, fallback);
        }

        public Dictionary<string, object?> Snapshot()
        {
            lock (_gate) return new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        }

        public void Update(IReadOnlyDictionary<string, object?> changes)
        {
            if (changes.Count > 256) throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
            foreach (var (name, value) in changes)
                if (name.Length is 0 or > 128 || name.Any(char.IsControl) ||
                    value is not (null or string or bool or double) ||
                    value is string { Length: > 4096 } || value is double number && !double.IsFinite(number))
                    throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
            TaskCompletionSource<long> signal;
            long revision;
            lock (_gate)
            {
                var next = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
                foreach (var (name, value) in changes) next[name] = value;
                if (next.Count > 256) throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                _values = next;
                revision = ++_revision;
                signal = _changed;
                _changed = NewSignal();
            }

            signal.TrySetResult(revision);
        }

        public static Dictionary<string, object?> Parse(IEnumerable<string> assignments)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var assignment in assignments)
            {
                var equals = assignment.IndexOf('=');
                if (equals <= 0 || result.Count >= 256)
                    throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                var text = assignment[(equals + 1)..];
                object? value = text switch
                {
                    "true" => true, "false" => false, "null" => null,
                    _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        => number,
                    _ => text
                };
                if (!result.TryAdd(assignment[..equals], value))
                    throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
            }

            var validated = new ScriptParameters();
            validated.Update(result);
            return result;
        }
    }
}
