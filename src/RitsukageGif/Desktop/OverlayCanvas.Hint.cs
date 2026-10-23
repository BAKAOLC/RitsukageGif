using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private const double HintMargin = 8;
        private const double HintPaddingX = 13;
        private const double HintPaddingY = 9;
        private static readonly IBrush HintBackground = new SolidColorBrush(Color.FromArgb(240, 25, 28, 35));
        private static readonly IPen HintBorder = new Pen(new SolidColorBrush(Color.FromRgb(140, 158, 180)));
        private FormattedText? _hintText;
        private string? _hintContent;
        private Size _hintLimit;

        public bool ShowControls { get; init; }
        public bool ShowSource { get; init; }
        public bool Controls { get; set; }
        public bool WindowSource { get; set; }

        private void DrawHint(DrawingContext context, Point mouse)
        {
            var limit = new Size(Bounds.Width - 2 * (HintMargin + HintPaddingX),
                Bounds.Height - 2 * (HintMargin + HintPaddingY));
            if (limit.Width < 1 || limit.Height < 16) return;
            var mode = UiText.Get(Mode switch
            {
                1 => "SnapHorizontal", 2 => "SnapVertical", 3 => "SnapBoth", _ => "SnapNone"
            });
            var content = UiText.Format("SelectionHint", mode, Selected.Width, Selected.Height);
            if (ShowControls)
                content += "\n" + UiText.Get(Controls ? "SelectionControls" : "SelectionWindows");
            if (ShowSource)
                content += "\n" + UiText.Get(WindowSource ? "SelectionWindowContent" : "SelectionScreenContent");
            if (_hintText is null || _hintContent != content || _hintLimit != limit)
            {
                _hintContent = content;
                _hintLimit = limit;
                _hintText = new FormattedText(content, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 13, Brushes.White)
                {
                    MaxTextWidth = limit.Width, MaxTextHeight = limit.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };
            }

            var size = new Size(Math.Min(limit.Width, Math.Ceiling(_hintText.WidthIncludingTrailingWhitespace)) +
                                2 * HintPaddingX,
                Math.Min(limit.Height, Math.Ceiling(_hintText.Height)) + 2 * HintPaddingY);
            var frame = GetHintFrame(Bounds.Size, size, mouse);
            context.DrawRectangle(HintBackground, HintBorder, frame, 4, 4);
            var textBounds = new Rect(frame.X + HintPaddingX, frame.Y + HintPaddingY,
                frame.Width - 2 * HintPaddingX, frame.Height - 2 * HintPaddingY);
            using (context.PushClip(textBounds))
                context.DrawText(_hintText, textBounds.TopLeft);
        }

        internal static Rect GetHintFrame(Size viewport, Size size, Point mouse)
        {
            var left = HintMargin;
            var top = HintMargin;
            var right = Math.Max(left, viewport.Width - HintMargin - size.Width);
            var bottom = Math.Max(top, viewport.Height - HintMargin - size.Height);
            var frame = new Rect(left, top, size.Width, size.Height);
            if (!frame.Inflate(20).Contains(mouse)) return frame;
            frame = new Rect(left, bottom, size.Width, size.Height);
            if (!frame.Inflate(20).Contains(mouse)) return frame;
            frame = new Rect(right, top, size.Width, size.Height);
            if (!frame.Inflate(20).Contains(mouse)) return frame;
            return new Rect(right, bottom, size.Width, size.Height);
        }
    }
}
