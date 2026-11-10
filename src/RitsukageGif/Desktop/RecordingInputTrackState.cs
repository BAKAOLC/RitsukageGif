using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingInputTrackState(bool keyboard, bool mouse)
    {
        private readonly Lock _sync = new();
        private int _available = (keyboard ? 1 : 0) | (mouse ? 2 : 0);
        private string? _unavailableReason;
        private (long Timestamp, long Keyboard, long Mouse)[] _timeline = [(0, keyboard ? 0 : -1, mouse ? 0 : -1)];

        internal bool KeyboardAvailable => (Volatile.Read(ref _available) & 1) != 0;
        internal bool MouseAvailable => (Volatile.Read(ref _available) & 2) != 0;
        internal string? UnavailableReason => Volatile.Read(ref _unavailableReason);

        internal void SetUnavailable(string reason)
        {
            lock (_sync)
            {
                Volatile.Write(ref _unavailableReason, reason);
                Volatile.Write(ref _available, 0);
                var previous = _timeline[^1];
                if (previous.Keyboard >= 0 || previous.Mouse >= 0)
                    Volatile.Write(ref _timeline, [.. _timeline, (Stopwatch.GetTimestamp(), -1, -1)]);
            }
        }

        internal bool SetKeyboard(bool enabled)
        {
            return SetEnabled(true, enabled);
        }

        internal bool SetMouse(bool enabled)
        {
            return SetEnabled(false, enabled);
        }

        internal (long Keyboard, long Mouse) At(long timestamp)
        {
            var timeline = Volatile.Read(ref _timeline);
            var low = 0;
            var high = timeline.Length - 1;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (timeline[middle].Timestamp <= timestamp) low = middle;
                else high = middle - 1;
            }

            return (timeline[low].Keyboard, timeline[low].Mouse);
        }

        private bool SetEnabled(bool keyboardTrack, bool enabled)
        {
            lock (_sync)
            {
                if (enabled && !(keyboardTrack ? KeyboardAvailable : MouseAvailable)) return false;
                var previous = _timeline[^1];
                if ((keyboardTrack ? previous.Keyboard : previous.Mouse) >= 0 == enabled) return enabled;
                var timestamp = Stopwatch.GetTimestamp();
                var next = (Timestamp: timestamp,
                    Keyboard: keyboardTrack ? enabled ? timestamp : -1 : previous.Keyboard,
                    Mouse: keyboardTrack ? previous.Mouse : enabled ? timestamp : -1);
                Volatile.Write(ref _timeline, [.. _timeline, next]);
                return enabled;
            }
        }
    }
}
