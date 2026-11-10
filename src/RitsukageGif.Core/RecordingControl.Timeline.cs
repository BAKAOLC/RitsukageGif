using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class RecordingControl
    {
        private readonly Dictionary<string, MediaTimeline> _timelines = new(StringComparer.Ordinal);
        private long _captureSequence;
        private long _stoppedAt = long.MaxValue;

        public bool TryMapInputTimestamp(long rawStopwatchTimestamp, out TimeSpan mediaPosition,
            string? streamName = null)
        {
            lock (_syncRoot)
            {
                mediaPosition = TimeSpan.Zero;
                if (!_timelines.TryGetValue(streamName ?? string.Empty, out var timeline) ||
                    timeline.StartedAt < 0 || rawStopwatchTimestamp < timeline.StartedAt ||
                    rawStopwatchTimestamp >= Interlocked.Read(ref _stoppedAt) ||
                    !TryMapTimestamp(rawStopwatchTimestamp, out var active) || active < timeline.Origin)
                    return false;
                mediaPosition = TimeSpan.FromSeconds((active - timeline.Origin) / (double)Stopwatch.Frequency);
                return true;
            }
        }

        public RecordingStatistics? GetStatistics(string? streamName = null)
        {
            lock (_syncRoot)
                return _timelines.TryGetValue(streamName ?? string.Empty, out var timeline) &&
                       timeline.Duration is { } duration
                    ? new(duration, timeline.AcceptedCaptures)
                    : null;
        }

        internal (long Sequence, object? Metadata) CaptureCompleted(object source)
        {
            return (Interlocked.Increment(ref _captureSequence),
                (source as IRecordingCaptureMetadataSource)?.CaptureMetadata);
        }

        internal void BindTimeline(long rawStart, long origin, string? streamName = null)
        {
            lock (_syncRoot)
            {
                var timeline = Timeline(streamName);
                timeline.StartedAt = rawStart;
                timeline.Origin = origin;
            }
        }

        internal void RecordStop()
        {
            Interlocked.CompareExchange(ref _stoppedAt, Stopwatch.GetTimestamp(), long.MaxValue);
        }

        internal void CompleteTimeline(TimeSpan duration, string? streamName = null)
        {
            RecordStop();
            lock (_syncRoot) Timeline(streamName).Duration = duration;
        }

        private MediaTimeline Timeline(string? streamName)
        {
            var key = streamName ?? string.Empty;
            if (_timelines.TryGetValue(key, out var timeline)) return timeline;
            timeline = new();
            _timelines.Add(key, timeline);
            return timeline;
        }
    }
}
