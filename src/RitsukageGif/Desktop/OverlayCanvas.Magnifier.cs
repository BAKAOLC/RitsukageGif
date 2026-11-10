using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private const double MagnifierWidth = 198;
        private const double MagnifierHeight = 126;
        private static readonly IPen MagnifierCrosshair = new Pen(Brushes.White);

        private static readonly IBrush MagnifierAxis = new SolidColorBrush(Color.FromArgb(110, 48, 137, 255));
        private static readonly IPen MagnifierPixelBorder = new Pen(Brushes.Black);

        private static readonly RenderOptions MagnifierOptions = new()
            { BitmapInterpolationMode = BitmapInterpolationMode.None };

        private SolidColorBrush? _magnifierColorBrush;
        private FormattedText? _magnifierColorText;
        private SampleColorContext _magnifierContext;
        private SampleColorTemplate? _magnifierFormat;

        private IPen _magnifierGrid = new Pen(new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)), .5);
        private string? _magnifierGridColor;
        private PixelPoint _magnifierPosition;
        private FormattedText? _magnifierPositionText;
        private SelectionCommandDefinition? _magnifierShortcutCommand;
        private string? _magnifierShortcutCulture;
        private SelectionCommandDefinition? _magnifierFormatShortcutCommand;
        private FormattedText? _magnifierFormatShortcutText;
        private FormattedText? _magnifierShortcutText;

        internal SampleColorTemplate SampleColorTemplate { get; set; } = SampleColorFormats.DefaultTemplate;
        internal bool RelativeCoordinates { get; set; }
        internal ScreenshotAnnotationSession? MagnifierAnnotations { get; set; }

        private PixelPoint SamplePosition => RelativeCoordinates && !Selected.IsEmpty
            ? new(Mouse.X - Selected.X, Mouse.Y - Selected.Y)
            : Mouse;

        private void DrawMagnifiedSelection(DrawingContext context, Rect lens, double factor)
        {
            if (Selected.IsEmpty) return;
            var region = new Rect(lens.Center.X + (Selected.X - (double)Mouse.X - .5) * factor,
                lens.Center.Y + (Selected.Y - (double)Mouse.Y - .5) * factor,
                Selected.Width * factor, Selected.Height * factor);
            if (CapturePreferences.MagnifierSelectionBorder)
                context.DrawRectangle(null, _selectedPen, region);
            if (!CapturePreferences.MagnifierSelectionHandles || IsCandidate ||
                _appearance?.SelectionAnchors == SelectionAnchorMode.None) return;
            Span<Point> points =
            [
                region.TopLeft, new(region.Center.X, region.Top), region.TopRight,
                new(region.Right, region.Center.Y), region.BottomRight,
                new(region.Center.X, region.Bottom), region.BottomLeft, new(region.Left, region.Center.Y),
            ];
            for (var index = 0; index < points.Length; index++)
            {
                if (_appearance?.SelectionAnchors == SelectionAnchorMode.Four && index % 2 != 0) continue;
                context.DrawEllipse(_selectionBrush, HandleBorder, points[index], 4 * Scale * factor,
                    4 * Scale * factor);
            }
        }

        private void DrawMagnifier(DrawingContext context, Rect lens)
        {
            using var clip = context.PushClip(lens);
            context.FillRectangle(Brushes.Black, lens);
            var factor = Math.Clamp(CapturePreferences.Magnification, 1, 100);
            var source = new Rect(Mouse.X - _monitor.X + .5 - lens.Width / factor / 2,
                Mouse.Y - _monitor.Y + .5 - lens.Height / factor / 2, lens.Width / factor, lens.Height / factor);
            var available = source.Intersect(new(0, 0, _monitor.Width, _monitor.Height));
            var destination = new Rect(lens.X + (available.X - source.X) * factor,
                lens.Y + (available.Y - source.Y) * factor, available.Width * factor, available.Height * factor);
            using (context.PushRenderOptions(CapturePreferences.SmoothMagnifier
                       ? new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }
                       : MagnifierOptions))
            {
                context.DrawImage(_bitmap, available, destination);
                if (WindowFrame is { } frame)
                {
                    var globalSource = source.Translate(new(_monitor.X, _monitor.Y));
                    var frameBounds = new Rect(frame.Region.X, frame.Region.Y, frame.Region.Width, frame.Region.Height);
                    var visible = globalSource.Intersect(frameBounds);
                    if (visible is { Width: > 0, Height: > 0 })
                        context.DrawImage(frame.Bitmap,
                            new(visible.X - frame.Region.X, visible.Y - frame.Region.Y, visible.Width, visible.Height),
                            new(lens.X + (visible.X - globalSource.X) * factor,
                                lens.Y + (visible.Y - globalSource.Y) * factor, visible.Width * factor,
                                visible.Height * factor));
                }
            }

            if (MagnifierAnnotations is { } annotations)
            {
                using var transform = context.PushTransform(new(factor, 0, 0, factor,
                    lens.Center.X - (Mouse.X + .5) * factor, lens.Center.Y - (Mouse.Y + .5) * factor));
                if (CapturePreferences.MagnifierAnnotations) annotations.Render(context);
                if (CapturePreferences.MagnifierAnnotationControls) annotations.RenderSelection(context, Scale);
            }

            var center = lens.Center;
            if (CapturePreferences.MagnifierCrosshair)
            {
                context.FillRectangle(MagnifierAxis, new(lens.Left, center.Y - factor / 2d, lens.Width, factor));
                context.FillRectangle(MagnifierAxis, new(center.X - factor / 2d, lens.Top, factor, lens.Height));
            }

            if (CapturePreferences.MagnifierGrid)
            {
                if (_magnifierGridColor != CapturePreferences.MagnifierGridColor)
                {
                    _magnifierGridColor = CapturePreferences.MagnifierGridColor;
                    _magnifierGrid = new Pen(new SolidColorBrush(Color.TryParse(_magnifierGridColor, out var color)
                        ? color
                        : Color.FromArgb(128, 128, 128, 128)), .5);
                }

                var startX = center.X - factor / 2d - Math.Ceiling(lens.Width / factor / 2) * factor;
                var startY = center.Y - factor / 2d - Math.Ceiling(lens.Height / factor / 2) * factor;
                for (var x = startX; x <= lens.Right; x += factor)
                    context.DrawLine(_magnifierGrid, new(x, lens.Top), new(x, lens.Bottom));
                for (var y = startY; y <= lens.Bottom; y += factor)
                    context.DrawLine(_magnifierGrid, new(lens.Left, y), new(lens.Right, y));
            }

            if (CapturePreferences.MagnifierCenterPixel)
            {
                context.DrawRectangle(null, MagnifierPixelBorder,
                    new(center.X - factor / 2d, center.Y - factor / 2d, factor, factor));
                context.DrawRectangle(null, MagnifierCrosshair,
                    new(center.X - factor / 2d - 1, center.Y - factor / 2d - 1, factor + 2, factor + 2));
            }

            DrawMagnifiedSelection(context, lens, factor);
            context.DrawRectangle(null, HintBorder, lens.Deflate(.5));
        }

        private void DrawMagnifierDetails(DrawingContext context, Rect frame)
        {
            var coordinates = SamplePosition;
            if (_magnifierPositionText is null || _magnifierPosition != coordinates)
            {
                _magnifierPosition = coordinates;
                _magnifierPositionText =
                    HintText($"({coordinates.X}, {coordinates.Y})", MagnifierWidth - 12, Brushes.White, 12, 22);
            }

            var position = _magnifierPositionText;
            if (CapturePreferences.MagnifierCoordinates)
                context.DrawText(position, new(frame.Center.X - position.Width / 2, frame.Top + MagnifierHeight + 5));
            if (!TryGetSampleColor(Mouse, out var color)) return;
            var sample = new SampleColorContext(color.R, color.G, color.B, color.A, Mouse, Selected);
            var inputs = SampleColorTemplate.Inputs;
            var contextChanged =
                (inputs.HasFlag(SampleColorTokenInputs.Pointer) && _magnifierContext.Pointer != Mouse) ||
                (inputs.HasFlag(SampleColorTokenInputs.Selection) && _magnifierContext.Selection != Selected);
            if (_magnifierColorBrush is null || _magnifierColorBrush.Color != color ||
                !ReferenceEquals(_magnifierFormat, SampleColorTemplate) || contextChanged)
            {
                _magnifierColorBrush = new(color);
                _magnifierFormat = SampleColorTemplate;
                _magnifierContext = sample;
                _magnifierColorText = HintText(SampleColorTemplate.Format(sample), MagnifierWidth - 34,
                    Brushes.White, 12, 22);
            }

            var label = _magnifierColorText!;
            var left = frame.Center.X - (label.Width + 24) / 2;
            context.DrawRectangle(_magnifierColorBrush, MagnifierCrosshair,
                new(left, frame.Top + MagnifierHeight + (CapturePreferences.MagnifierCoordinates ? 29 : 7), 15, 15));
            context.DrawText(label,
                new(left + 23, frame.Top + MagnifierHeight + (CapturePreferences.MagnifierCoordinates ? 28 : 6)));
            if (!CapturePreferences.MagnifierShortcuts) return;
            var count = UpdateMagnifierShortcuts();
            var y = frame.Bottom - count * 20 - 4;
            if (_magnifierFormatShortcutText is { } formatHint)
            {
                context.DrawText(formatHint, new(frame.Center.X - formatHint.Width / 2, y));
                y += 20;
            }

            if (_magnifierShortcutText is { } copyHint)
                context.DrawText(copyHint, new(frame.Center.X - copyHint.Width / 2, y));
        }

        private int UpdateMagnifierShortcuts()
        {
            var bindings = ShortcutBindings ?? SelectionCommands.SnapshotBindings();
            SelectionCommandDefinition? copy = null, format = null;
            foreach (var binding in bindings)
            {
                if (copy is null && binding.Command == SelectionCommand.CopyColor) copy = binding;
                if (format is null && binding.Command == SelectionCommand.CycleColorFormat) format = binding;
                if (copy is not null && format is not null) break;
            }

            var culture = CultureInfo.CurrentUICulture.Name;
            if (_magnifierShortcutCommand != copy || _magnifierFormatShortcutCommand != format ||
                _magnifierShortcutCulture != culture)
            {
                _magnifierShortcutCommand = copy;
                _magnifierFormatShortcutCommand = format;
                _magnifierShortcutCulture = culture;
                _magnifierShortcutText = ShortcutHint(copy);
                _magnifierFormatShortcutText = ShortcutHint(format);
            }

            return (_magnifierShortcutText is null ? 0 : 1) + (_magnifierFormatShortcutText is null ? 0 : 1);

            static FormattedText? ShortcutHint(SelectionCommandDefinition? command)
            {
                return command is null || string.IsNullOrEmpty(command.Gesture)
                    ? null
                    : HintText($"{command.Gesture}: {UiText.Get(command.DescriptionKey)}", MagnifierWidth - 16,
                        MutedText, 11, 20);
            }
        }

        internal bool TryGetSampleColor(PixelPoint point, out Color color)
        {
            var pixels = _pixels;
            var region = _monitor;
            if (WindowFrame is { } window && window.Region.Contains(point))
            {
                pixels = window.Pixels;
                region = window.Region;
            }

            color = default;
            if (!region.Contains(point)) return false;
            var offset = ((point.Y - region.Y) * region.Width + point.X - region.X) * 4;
            color = Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            return true;
        }
    }
}
