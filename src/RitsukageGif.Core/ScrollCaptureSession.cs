using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.ScrollCapture");
        private readonly int _axisLength;
        private readonly int _crossLength;
        private readonly int _height;
        private readonly ScrollCaptureOptions _options;
        private readonly int _rowBytes;
        private readonly byte[] _scratch;
        private readonly int _width;
        private long _attemptedFrames;
        private int? _crossPosition;
        private int _end;
        private bool _limitReported;
        private long _nextLogAt;
        private int _rowStripIndex;
        private List<Strip> _strips = [];
        private UndoState? _undo;

        public ScrollCaptureSession(int width, int height, ScrollCaptureOptions? options = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            _options = options ?? new ScrollCaptureOptions();
            if (!Enum.IsDefined(_options.Axis) || _options.LeadingInset < 0 || _options.TrailingInset < 0
                || _options.MinimumOverlap is < 0.2 or > 0.8 || !double.IsFinite(_options.MinimumOverlap)
                || _options.MaximumLength < 1 || _options.MaximumBytes < 1)
                throw new ArgumentException("Invalid scrolling capture options.", nameof(options));
            _width = width;
            _height = height;
            var vertical = _options.Axis == ScrollCaptureAxis.Vertical;
            _crossLength = vertical ? width : height;
            _axisLength = checked((vertical ? height : width) - _options.LeadingInset - _options.TrailingInset);
            if (_axisLength < 16 || _crossLength < 8)
                throw new ArgumentException("The capture area is too small after cropping.", nameof(options));
            _rowBytes = checked(_crossLength * 4);
            if (_axisLength > _options.MaximumLength || (long)_rowBytes * _axisLength * 3 > _options.MaximumBytes)
                throw new ArgumentException("The capture area exceeds the memory or length limit.", nameof(options));
            _scratch = new byte[checked(_axisLength * _rowBytes)];
            _nextLogAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            Logger.ZLogInformation(
                $"Scrolling capture session created: width={width}, height={height}, axis={_options.Axis.ToString()}, maximumLength={_options.MaximumLength}, maximumBytes={_options.MaximumBytes}");
        }

        public bool CanUndo => _undo is not null;
        public int StartOffset { get; private set; }

        public int CurrentOffset { get; private set; }

        public int Width => _options.Axis == ScrollCaptureAxis.Vertical ? _crossLength : _end - StartOffset;
        public int Height => _options.Axis == ScrollCaptureAxis.Vertical ? _end - StartOffset : _crossLength;

        public ScrollCaptureResult TryAppend(ReadOnlySpan<byte> bgra, int screenX, int screenY,
            CancellationToken token = default)
        {
            if (bgra.Length != (long)_width * _height * 4)
                throw new ArgumentException("The frame dimensions must remain unchanged.", nameof(bgra));
            token.ThrowIfCancellationRequested();
            _attemptedFrames++;
            var crossPosition = _options.Axis == ScrollCaptureAxis.Vertical ? screenX : screenY;
            if (_crossPosition is { } previous && crossPosition != previous)
                return Result(ScrollCaptureStatus.InvalidPosition);
            var pixels = Normalize(bgra);
            token.ThrowIfCancellationRequested();
            if (_strips.Count == 0)
            {
                _strips.Add(new(0, _axisLength, (byte[])pixels.Clone()));
                _end = _axisLength;
                _crossPosition = crossPosition;
                return Result(ScrollCaptureStatus.Started, 0, _axisLength, 1);
            }

            var match = FindMatch(pixels, token);
            if (match.Status != ScrollCaptureStatus.Appended) return match;
            var offset = match.Offset;
            var confidence = match.Confidence;
            var nextStart = Math.Min(StartOffset, offset);
            var nextEnd = Math.Max(_end, checked(offset + _axisLength));
            var added = nextEnd - nextStart - (_end - StartOffset);
            if (added == 0)
            {
                CurrentOffset = offset;
                return Result(ScrollCaptureStatus.AlreadyCovered, offset, 0, confidence);
            }

            // Keep space for both the assembled export and the incoming frame.
            if (nextEnd - nextStart > _options.MaximumLength
                || ((long)(nextEnd - nextStart) * 2 + _axisLength) * _rowBytes > _options.MaximumBytes)
                return Result(ScrollCaptureStatus.LimitReached, offset);
            token.ThrowIfCancellationRequested();
            var prepend = nextStart < StartOffset;
            var begin = prepend ? offset : _end;
            var end = prepend ? StartOffset : nextEnd;
            var strip = new Strip(begin, end,
                [.. pixels.AsSpan((begin - offset) * _rowBytes, (end - begin) * _rowBytes)]);
            var undo = new UndoState(StartOffset, _end, CurrentOffset, prepend);
            _strips.Insert(prepend ? 0 : _strips.Count, strip);
            _undo = undo;
            StartOffset = nextStart;
            _end = nextEnd;
            CurrentOffset = offset;
            return Result(ScrollCaptureStatus.Appended, offset, added, confidence);
        }

        // ReSharper disable once UnusedMember.Global
        public bool Undo()
        {
            if (_undo is not { } state) return false;
            if (state.RestoreStrips is { } original) _strips = original;
            else _strips.RemoveAt(state.Prepend ? 0 : _strips.Count - 1);
            StartOffset = state.Start;
            _end = state.End;
            CurrentOffset = state.LastOffset;
            _undo = null;
            _rowStripIndex = 0;
            Logger.ZLogDebug(
                $"Scrolling capture edit undone: width={Width}, height={Height}, strips={_strips.Count}");
            return true;
        }

        public ScrollCaptureImage ExportPixels(CancellationToken token = default)
        {
            if (_strips.Count == 0) throw new InvalidOperationException("No frame has been accepted.");
            token.ThrowIfCancellationRequested();
            Logger.ZLogDebug(
                $"Scrolling capture export started: width={Width}, height={Height}, strips={_strips.Count}");
            var output = new byte[checked(Width * Height * 4)];
            foreach (var strip in _strips)
            {
                token.ThrowIfCancellationRequested();
                if (_options.Axis == ScrollCaptureAxis.Vertical)
                    strip.Pixels.AsSpan(strip.DataOffset, (strip.End - strip.Start) * _rowBytes)
                        .CopyTo(output.AsSpan((strip.Start - StartOffset) * _rowBytes));
                else
                    for (var axis = strip.Start; axis < strip.End; axis++)
                    for (var cross = 0; cross < _crossLength; cross++)
                        strip.Pixels.AsSpan(strip.DataOffset + (axis - strip.Start) * _rowBytes + cross * 4, 4)
                            .CopyTo(output.AsSpan((cross * Width + axis - StartOffset) * 4, 4));
            }

            Logger.ZLogInformation(
                $"Scrolling capture export completed: width={Width}, height={Height}, bytes={output.Length}, inputFrames={_attemptedFrames}, strips={_strips.Count}");
            return new(output, Width, Height);
        }

        private byte[] Normalize(ReadOnlySpan<byte> pixels)
        {
            if (_options.Axis == ScrollCaptureAxis.Vertical)
            {
                pixels.Slice(_options.LeadingInset * _rowBytes, _axisLength * _rowBytes).CopyTo(_scratch);
                return _scratch;
            }

            for (var axis = 0; axis < _axisLength; axis++)
            for (var cross = 0; cross < _crossLength; cross++)
                pixels.Slice((cross * _width + axis + _options.LeadingInset) * 4, 4)
                    .CopyTo(_scratch.AsSpan(axis * _rowBytes + cross * 4, 4));
            return _scratch;
        }

        private ScrollCaptureResult Result(ScrollCaptureStatus status, int offset = 0, int added = 0,
            double confidence = 0)
        {
            switch (status)
            {
                case ScrollCaptureStatus.Started:
                    Logger.ZLogInformation(
                        $"Scrolling capture accepted its first frame: width={Width}, height={Height}, axis={_options.Axis.ToString()}");
                    break;
                case ScrollCaptureStatus.LimitReached:
                    if (!_limitReported)
                    {
                        _limitReported = true;
                        Logger.ZLogWarning(
                            $"Scrolling capture reached its configured limit: width={Width}, height={Height}, strips={_strips.Count}");
                    }

                    break;
                case ScrollCaptureStatus.Appended:
                case ScrollCaptureStatus.AlreadyCovered:
                case ScrollCaptureStatus.Unmatched:
                case ScrollCaptureStatus.Ambiguous:
                case ScrollCaptureStatus.InvalidPosition:
                default:
                    break;
            }

            if (Logger.IsEnabled(LogLevel.Debug) && Stopwatch.GetTimestamp() >= _nextLogAt)
            {
                _nextLogAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
                Logger.ZLogDebug(
                    $"Scrolling capture progress: status={status.ToString()}, width={Width}, height={Height}, offset={offset}, added={added}, confidence={confidence}, inputFrames={_attemptedFrames}, strips={_strips.Count}");
            }

            return new(status, offset, added, confidence, Width, Height);
        }

        private ReadOnlySpan<byte> Row(int position)
        {
            if (_rowStripIndex < _strips.Count)
            {
                var cached = _strips[_rowStripIndex];
                if (position >= cached.Start && position < cached.End)
                    return cached.Pixels.AsSpan(cached.DataOffset + (position - cached.Start) * _rowBytes, _rowBytes);
            }

            var low = 0;
            var high = _strips.Count - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var strip = _strips[middle];
                if (position < strip.Start)
                {
                    high = middle - 1;
                }
                else if (position >= strip.End)
                {
                    low = middle + 1;
                }
                else
                {
                    _rowStripIndex = middle;
                    return strip.Pixels.AsSpan(strip.DataOffset + (position - strip.Start) * _rowBytes, _rowBytes);
                }
            }

            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }
}
