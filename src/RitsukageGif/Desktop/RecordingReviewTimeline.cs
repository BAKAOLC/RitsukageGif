using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop

{
    internal sealed class RecordingReviewTimeline : Control

    {
        private int _drag;

        private double _duration = 1;

        internal RecordingReviewTimeline()

        {
            Height = 28;

            MinWidth = 160;

            Focusable = true;
        }

        private double Position { get; set; }

        internal double RangeStart { get; private set; }

        internal double RangeEnd { get; private set; } = 1;

        internal event Action<double>? SeekRequested;

        internal event Action? RangeChanged;

        internal void SetDuration(double duration)

        {
            _duration = Math.Max(0.001, duration);

            RangeStart = 0;

            RangeEnd = _duration;

            SetPosition(0);
        }

        internal void SetPosition(double position)

        {
            Position = Math.Clamp(position, RangeStart, RangeEnd);

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)

        {
            base.Render(context);

            var y = Bounds.Height / 2;

            var start = X(RangeStart);

            var end = X(RangeEnd);

            context.DrawLine(new Pen(AppTheme.Outline, 4), new(8, y), new(Bounds.Width - 8, y));

            context.DrawLine(new Pen(AppTheme.Accent, 4), new(start, y), new(end, y));

            context.DrawRectangle(AppTheme.Ink, null, new(start - 2, y - 9, 4, 18), 1, 1);

            context.DrawRectangle(AppTheme.Ink, null, new(end - 2, y - 9, 4, 18), 1, 1);

            context.DrawEllipse(AppTheme.Accent, new Pen(AppTheme.Surface), new(X(Position), y), 5, 5);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs args)

        {
            base.OnPointerPressed(args);

            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            var x = args.GetPosition(this).X;

            _drag = Math.Abs(x - X(RangeStart)) <= 7 ? 1 : Math.Abs(x - X(RangeEnd)) <= 7 ? 2 : 3;

            args.Pointer.Capture(this);

            Focus();

            Apply(x);

            args.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs args)

        {
            base.OnPointerMoved(args);

            if (_drag != 0) Apply(args.GetPosition(this).X);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs args)

        {
            base.OnPointerReleased(args);

            if (_drag == 0) return;

            _drag = 0;

            args.Pointer.Capture(null);

            args.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs args)

        {
            _drag = 0;

            base.OnPointerCaptureLost(args);
        }

        private double X(double seconds)
        {
            return 8 + seconds / _duration * Math.Max(1, Bounds.Width - 16);
        }

        private void Apply(double x)

        {
            var time = Math.Clamp((x - 8) / Math.Max(1, Bounds.Width - 16) * _duration, 0, _duration);

            switch (_drag)

            {
                case 1:

                    RangeStart = Math.Min(time, Math.Max(0, RangeEnd - 0.001));

                    RangeChanged?.Invoke();

                    break;

                case 2:

                    RangeEnd = Math.Max(time, Math.Min(_duration, RangeStart + 0.001));

                    RangeChanged?.Invoke();

                    break;

                default:

                    SetPosition(time);

                    SeekRequested?.Invoke(Position);

                    break;
            }

            SetPosition(Position);
        }
    }
}
