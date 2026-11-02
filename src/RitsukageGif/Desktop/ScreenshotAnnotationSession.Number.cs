using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly (int Value, string Text)[] RomanDigits =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];

        private void CompleteNumber(int number)
        {
            _nextNumber = Math.Min(999999, number + 1);
            if (_behavior.GlobalNumberCounter)
                Interlocked.Exchange(ref _globalNumber, _nextNumber - (_behavior.NumberStartsAtZero ? 0 : 1));
        }

        private async Task ReplaceNumberPropertiesAsync(ScreenshotAnnotation original, ScreenshotAnnotation replacement)
        {
            await ReplaceAnnotationAsync(_annotations.IndexOf(original), replacement);
            SynchronizeNumberCounter();
            Changed?.Invoke();
        }

        private bool CanEditNumberTarget(ScreenshotAnnotation annotation)
        {
            return annotation is { Tool: ScreenshotTool.Number, Points.Count: > 1 } &&
                   CanEditAnnotation(annotation, ReferenceEquals(annotation, _selected)) &&
                   _behavior.ControlPointVisibility != AnnotationControlPointVisibility.Hidden;
        }

        private bool ShowNumberTarget(ScreenshotAnnotation annotation)
        {
            return CanEditNumberTarget(annotation) &&
                   (_behavior.ControlPointVisibility == AnnotationControlPointVisibility.Always ||
                    (_hoverPoint is { } hover && AnnotationObjectBounds(annotation).Inflate(ControlHoverMargin)
                        .Contains(new Point(hover.X, hover.Y))));
        }

        private static bool HitNumberCaption(ScreenshotAnnotation annotation, PixelPoint point)
        {
            var layout = NumberLayout(annotation);
            return layout.CaptionText is { } caption &&
                   new Rect(layout.CaptionOrigin, new Size(caption.Width, caption.Height)).Contains(new Point(point.X,
                       point.Y));
        }

        private void MoveNumberCaption(ScreenshotAnnotation original, PixelPoint point)
        {
            if (point == _moveStart && ReferenceEquals(_selected, original)) return;
            var index = _annotations.IndexOf(_selected!);
            if (index < 0) return;
            var replacement = ReferenceEquals(_selected, original) ? CloneAnnotation(original) : _selected!;
            var origin = NumberLayout(original).CaptionOrigin;
            var target = new Point(origin.X + point.X - _moveStart.X, origin.Y + point.Y - _moveStart.Y);
            if (replacement.Points.Count < 2) replacement.Points.Add(original.Points[0]);
            if (replacement.Points.Count < 3) replacement.Points.Add(target);
            else replacement.Points[2] = target;
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }

        private static string FormatNumber(int value, AnnotationNumberFormat format)
        {
            if (value == 0) return format == AnnotationNumberFormat.Chinese ? "零" : "0";
            switch (format)
            {
                case AnnotationNumberFormat.Roman:
                    var roman = new StringBuilder();
                    foreach (var (number, text) in RomanDigits)
                        while (value >= number)
                        {
                            roman.Append(text);
                            value -= number;
                        }

                    return roman.ToString();
                case AnnotationNumberFormat.LowercaseLetters:
                case AnnotationNumberFormat.UppercaseLetters:
                    Span<char> letters = stackalloc char[8];
                    var position = letters.Length;
                    var first = format == AnnotationNumberFormat.LowercaseLetters ? 'a' : 'A';
                    while (value > 0)
                    {
                        value--;
                        letters[--position] = (char)(first + value % 26);
                        value /= 26;
                    }

                    return new(letters[position..]);
                case AnnotationNumberFormat.Chinese:
                    var chinese = new StringBuilder();
                    var high = value / 10000;
                    var low = value % 10000;
                    if (high > 0)
                    {
                        AppendChineseSection(chinese, high);
                        chinese.Append('万');
                        if (low is > 0 and < 1000) chinese.Append('零');
                    }

                    if (low > 0) AppendChineseSection(chinese, low);
                    return chinese.ToString();
                case AnnotationNumberFormat.Decimal:
                default:
                    return value.ToString(CultureInfo.CurrentCulture);
            }
        }

        private static void AppendChineseSection(StringBuilder result, int value)
        {
            const string digits = "零一二三四五六七八九";
            const string units = "千百十";
            var sectionStart = result.Length;
            var zero = false;
            for (var place = 1000; place >= 1; place /= 10)
            {
                var digit = value / place;
                value %= place;
                if (digit == 0)
                {
                    zero |= result.Length > sectionStart && value > 0;
                    continue;
                }

                if (zero) result.Append('零');
                zero = false;
                if (!(digit == 1 && place == 10 && result.Length == 0)) result.Append(digits[digit]);
                if (place > 1) result.Append(units[place switch { 1000 => 0, 100 => 1, _ => 2 }]);
            }
        }

        private static AnnotationNumberLayout NumberLayout(ScreenshotAnnotation annotation)
        {
            var center = annotation.Points[0];
            var target = annotation.Points.Count > 1 ? annotation.Points[1] : center;
            var captionAnchor = annotation.Points.Count > 2 ? annotation.Points[2] : (Point?)null;
            var properties = annotation.Properties;
            if (annotation.NumberLayout is { } cached && cached.Properties == properties &&
                cached.Color == annotation.Color && cached.FontSize.Equals(annotation.FontSize) &&
                cached.Thickness.Equals(annotation.Thickness) && cached.Caption == annotation.Text &&
                cached.Center == center && cached.Target == target && cached.CaptionAnchor == captionAnchor)
                return cached;

            var ink = new SolidColorBrush(annotation.Color);
            var outlineColor = properties.NumberAutomaticOutline
                ? .2126 * annotation.Color.R + .7152 * annotation.Color.G + .0722 * annotation.Color.B > 150
                    ? Colors.Black
                    : Colors.White
                : properties.NumberOutlineColor;
            var contrast = new SolidColorBrush(outlineColor);
            var typeface = CreateTypeface(properties);
            var textValue = FormatNumber(properties.Number, properties.NumberFormat);
            var diameter = annotation.FontSize * 1.7;
            var label = new FormattedText(textValue, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, annotation.FontSize, contrast);
            if (properties.NumberStyle != AnnotationNumberStyle.Plain &&
                Math.Max(label.Width, label.Height) > diameter * .8)
                label = new(textValue, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                    annotation.FontSize * diameter * .8 / Math.Max(label.Width, label.Height), contrast);
            var markerBounds = properties.NumberStyle == AnnotationNumberStyle.Plain
                ? new Rect(center - new Vector(label.Width / 2, label.Height / 2), new Size(label.Width, label.Height))
                : new Rect(center - new Vector(diameter / 2, diameter / 2), new Size(diameter, diameter));
            Geometry? marker = properties.NumberStyle switch
            {
                AnnotationNumberStyle.Plain => null,
                AnnotationNumberStyle.RoundedSquare => new RectangleGeometry(markerBounds, diameter * .15,
                    diameter * .15),
                _ => new EllipseGeometry(markerBounds),
            };
            if (properties.NumberStyle is AnnotationNumberStyle.Plain or AnnotationNumberStyle.OutlineCircle)
                label.SetForegroundBrush(properties.NumberAutomaticOutline ? ink : contrast);
            var bounds = markerBounds.Inflate(Math.Max(1, annotation.Thickness / 2));
            var delta = new Vector(target.X - center.X, target.Y - center.Y);
            var leaderStart = center;
            var leaderEnd = center;
            Geometry? head = null, callout = null;
            var filledHead = false;
            if (delta.Length > diameter / 2 + 2)
            {
                var direction = delta / delta.Length;
                var radius =
                    properties.NumberStyle is AnnotationNumberStyle.RoundedSquare or AnnotationNumberStyle.Plain
                        ? Math.Min(markerBounds.Width / 2 / Math.Abs(direction.X),
                            markerBounds.Height / 2 / Math.Abs(direction.Y))
                        : diameter / 2;
                leaderStart = center + direction * radius;
                leaderEnd = target;
                if (properties.NumberCallout)
                {
                    var side = new Vector(-direction.Y, direction.X) * Math.Min(diameter / 4, annotation.Thickness * 2);
                    var path = new StreamGeometry();
                    using (var figure = path.Open())
                    {
                        figure.BeginFigure(leaderStart + side);
                        figure.LineTo(target);
                        figure.LineTo(leaderStart - side);
                        figure.EndFigure(true);
                    }

                    callout = path;
                }
                else
                {
                    (head, filledHead) =
                        EndpointGeometry(properties.EndEndpoint, target, direction, annotation.Thickness);
                    var trim = Math.Min(EndpointTrim(properties.EndEndpoint, annotation.Thickness),
                        Math.Max(0, delta.Length - radius));
                    leaderEnd = target - direction * trim;
                }

                bounds = bounds.Union(new Rect(leaderStart, target).Normalize().Inflate(annotation.Thickness));
                if (head is not null) bounds = bounds.Union(head.Bounds.Inflate(annotation.Thickness));
                if (callout is not null) bounds = bounds.Union(callout.Bounds.Inflate(annotation.Thickness));
            }

            FormattedText? caption = null, captionOutline = null;
            var captionOrigin = new Point();
            if (!string.IsNullOrEmpty(annotation.Text))
            {
                caption = new(annotation.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, annotation.FontSize * properties.NumberCaptionScale, ink);
                captionOrigin = captionAnchor ?? new(markerBounds.Right + 8, center.Y - caption.Height / 2);
                if (properties.NumberCaptionOutline)
                    captionOutline = new(annotation.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        typeface, annotation.FontSize * properties.NumberCaptionScale, contrast);
                bounds = bounds.Union(new Rect(captionOrigin, new Size(caption.Width, caption.Height))
                    .Inflate(Math.Max(2, annotation.Thickness / 2 + 2)));
            }

            return annotation.NumberLayout = new()
            {
                Properties = properties, Color = annotation.Color, FontSize = annotation.FontSize,
                Thickness = annotation.Thickness, Caption = annotation.Text, Center = center, Target = target,
                CaptionAnchor = captionAnchor,
                MarkerBounds = markerBounds, Bounds = bounds, Marker = marker, Label = label,
                LabelOrigin = new(center.X - label.Width / 2, center.Y - label.Height / 2), Ink = ink,
                Outline = new Pen(properties is
                    { NumberStyle: AnnotationNumberStyle.OutlineCircle, NumberAutomaticOutline: true }
                    ? ink
                    : contrast, Math.Max(1, annotation.Thickness / 2)),
                LeaderPen = new Pen(ink, annotation.Thickness, lineCap: PenLineCap.Round),
                LeaderStart = leaderStart, LeaderEnd = leaderEnd, LeaderHead = head, FilledHead = filledHead,
                Callout = callout, CaptionText = caption, CaptionOutline = captionOutline,
                CaptionOrigin = captionOrigin,
            };
        }

        private static void DrawNumber(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var layout = NumberLayout(annotation);
            var style = annotation.Properties.NumberStyle;
            if (layout.Callout is { } callout)
            {
                context.DrawGeometry(layout.Ink, null, callout);
            }
            else if (layout.LeaderStart != layout.LeaderEnd)
            {
                context.DrawLine(layout.LeaderPen, layout.LeaderStart, layout.LeaderEnd);
                if (layout.LeaderHead is { } head)
                    context.DrawGeometry(layout.FilledHead ? layout.Ink : null, layout.LeaderPen, head);
            }

            if (layout.Marker is { } marker)
                context.DrawGeometry(style == AnnotationNumberStyle.OutlineCircle ? null : layout.Ink,
                    style is AnnotationNumberStyle.BorderedCircle or AnnotationNumberStyle.OutlineCircle
                        ? layout.Outline
                        : null, marker);
            context.DrawText(layout.Label, layout.LabelOrigin);
            if (layout.CaptionText is not { } caption) return;
            if (layout.CaptionOutline is { } outline)
                for (var direction = 0; direction < 8; direction++)
                {
                    var angle = direction * Math.PI / 4;
                    context.DrawText(outline, layout.CaptionOrigin + new Vector(Math.Cos(angle), Math.Sin(angle)));
                }

            context.DrawText(caption, layout.CaptionOrigin);
            if (annotation.Properties.NumberCaptionGuide)
            {
                var start = layout.CaptionOrigin + new Vector(0, caption.Height + 2);
                var end = layout.CaptionOrigin + new Vector(caption.Width, caption.Height + 2);
                var nearest = new Point(Math.Clamp(layout.Center.X, start.X, end.X), start.Y);
                var delta = new Vector(nearest.X - layout.Center.X, nearest.Y - layout.Center.Y);
                if (delta.Length > layout.MarkerBounds.Width / 2)
                    context.DrawLine(layout.LeaderPen,
                        layout.Center + delta / delta.Length * (layout.MarkerBounds.Width / 2), nearest);
                context.DrawLine(layout.LeaderPen, start, end);
            }
        }

        private static Rect NumberRenderBounds(ScreenshotAnnotation annotation)
        {
            return NumberLayout(annotation).Bounds;
        }

        private bool NumberHitTest(ScreenshotAnnotation annotation, Point point)
        {
            var layout = NumberLayout(annotation);
            if (layout.Marker?.FillContains(point) ?? layout.MarkerBounds.Contains(point)) return true;
            if (layout.CaptionText is { } caption &&
                new Rect(layout.CaptionOrigin, new Size(caption.Width, caption.Height)).Contains(point)) return true;
            return layout.LeaderStart != layout.LeaderEnd &&
                   SegmentDistance(point, layout.LeaderStart, layout.Target) <= Math.Max(5 * InteractionScale,
                       annotation.Thickness / 2 + 3 * InteractionScale);
        }
    }
}
