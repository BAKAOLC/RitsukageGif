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
        private SelectionCommandDefinition? _magnifierCoordinateShortcutCommand;
        private FormattedText? _magnifierCoordinateShortcutText;

        internal SampleColorTemplate SampleColorTemplate { get; set; } = SampleColorFormats.DefaultTemplate;
        internal bool RelativeCoordinates { get; set; }
        internal ScreenshotAnnotationSession? MagnifierAnnotations { get; set; }

        private PixelPoint SamplePosition => RelativeCoordinates && !Selected.IsEmpty
            ? new(Mouse.X - Selected.X, Mouse.Y - Selected.Y)
            : Mouse;

        private bool MagnifierSelectionHandlesVisible =>
            CapturePreferences.MagnifierSelectionHandles && Snapshot?.Phase == SelectionPhase.Selected &&
            SelectionHandlesVisible;

        private bool ShowsMagnifierCoordinateShortcut => IsScreenshot && !Selected.IsEmpty &&
                                                         Selected != _monitor &&
                                                         (Selected.X != 0 || Selected.Y != 0);

        private void DrawMagnifiedSelection(DrawingContext context, Rect lens, double factor, PixelPoint point)
        {
            if (Selected.IsEmpty) return;
            var border = CapturePreferences.MagnifierSelectionBorder;
            var handles = MagnifierSelectionHandlesVisible;
            if (!border && !handles) return;
            var intersection = Selected.Intersect(_monitor);
            if (intersection.IsEmpty && !IsScreenshotRectangle) return;
            var scale = Scale * factor;
            using var transform = context.PushTransform(new(scale, 0, 0, scale,
                lens.Center.X - (point.X - (double)_monitor.X + .5) * factor,
                lens.Center.Y - (point.Y - (double)_monitor.Y + .5) * factor));
            if (border)
            {
                var local = intersection.ToLocal(_monitor, Scale);
                DrawSelectionBorder(context, intersection, new(local.X, local.Y, local.Width, local.Height));
            }

            if (handles) DrawSelectionHandles(context);
        }

        private bool MagnifierHandlesMayCover(Rect region)
        {
            var outline = IsScreenshotRectangle ? ConfiguredBorderWidth / 2 : 0;
            var left = Selected.X - outline;
            var right = Selected.Right + outline;
            var top = Selected.Y - outline;
            var bottom = Selected.Bottom + outline;
            var coverage = region.Inflate(SelectionScenePadding(null, true) - outline);
            var radius = ScreenshotMarkerRadius;
            var size = Math.Min(right - left, bottom - top);
            if ((!IsScreenshotRectangle || (FrameRadiusPixels == 0 && size >= 4 * radius * Scale + 10)) &&
                (coverage.Contains(new Point(left, top)) || coverage.Contains(new Point(right, top)) ||
                 coverage.Contains(new Point(right, bottom)) || coverage.Contains(new Point(left, bottom))))
                return true;
            if (_appearance?.SelectionAnchors == SelectionAnchorMode.Four ||
                (IsScreenshotRectangle &&
                 (size < 2 * radius * Scale || Math.Min(Selected.Width, Selected.Height) <= ConfiguredBorderWidth)))
                return false;
            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            return coverage.Contains(new Point(centerX, top)) || coverage.Contains(new Point(centerX, bottom)) ||
                   coverage.Contains(new Point(left, centerY)) || coverage.Contains(new Point(right, centerY));
        }

        private void DrawMagnifier(DrawingContext context, Rect lens)
        {
            using var clip = context.PushClip(lens);
            context.FillRectangle(Brushes.Black, lens);
            var factor = Math.Clamp(CapturePreferences.Magnification, 1, 100);
            var source = new Rect(Mouse.X + .5 - lens.Width / factor / 2,
                Mouse.Y + .5 - lens.Height / factor / 2, lens.Width / factor, lens.Height / factor);
            var region = MagnifierSceneRaster.Region(new(Mouse.X, Mouse.Y), source.Size);
            using (context.PushRenderOptions(CapturePreferences.SmoothMagnifier
                       ? new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }
                       : MagnifierOptions))
                if (MagnifierSceneMayCover(region))
                    MagnifierSceneRaster.Draw(context, source, lens, region,
                        _magnifierScenePainter ??= PaintMagnifierScene);
                else DrawMagnifierBackground(context, source, lens, factor);

            var center = lens.Center;
            if (CapturePreferences.MagnifierCrosshair)
            {
                var half = factor / 2d;
                var armWidth = lens.Width / 2 - half;
                var armHeight = lens.Height / 2 - half;
                context.FillRectangle(MagnifierAxis, new(lens.Left, center.Y - half, armWidth, factor));
                context.FillRectangle(MagnifierAxis, new(center.X + half, center.Y - half, armWidth, factor));
                context.FillRectangle(MagnifierAxis, new(center.X - half, lens.Top, factor, armHeight));
                context.FillRectangle(MagnifierAxis, new(center.X - half, center.Y + half, factor, armHeight));
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
            {
                context.DrawText(copyHint, new(frame.Center.X - copyHint.Width / 2, y));
                y += 20;
            }

            if (ShowsMagnifierCoordinateShortcut && _magnifierCoordinateShortcutText is { } coordinateHint)
                context.DrawText(coordinateHint, new(frame.Center.X - coordinateHint.Width / 2, y));
        }

        private int UpdateMagnifierShortcuts()
        {
            var bindings = ShortcutBindings ?? SelectionCommands.SnapshotBindings();
            SelectionCommandDefinition? copy = null, format = null, coordinates = null;
            foreach (var binding in bindings)
            {
                if (copy is null && binding.Command == SelectionCommand.CopyColor) copy = binding;
                if (format is null && binding.Command == SelectionCommand.CycleColorFormat) format = binding;
                if (coordinates is null && binding.Command == SelectionCommand.ToggleCoordinateMode)
                    coordinates = binding;
                if (copy is not null && format is not null && coordinates is not null) break;
            }

            var culture = CultureInfo.CurrentUICulture.Name;
            if (_magnifierShortcutCommand != copy || _magnifierFormatShortcutCommand != format ||
                _magnifierCoordinateShortcutCommand != coordinates ||
                _magnifierShortcutCulture != culture)
            {
                _magnifierShortcutCommand = copy;
                _magnifierFormatShortcutCommand = format;
                _magnifierShortcutCulture = culture;
                _magnifierShortcutText = ShortcutHint(copy);
                _magnifierFormatShortcutText = ShortcutHint(format);
                _magnifierCoordinateShortcutCommand = coordinates;
                _magnifierCoordinateShortcutText = ShortcutHint(coordinates);
            }

            return (_magnifierShortcutText is null ? 0 : 1) + (_magnifierFormatShortcutText is null ? 0 : 1) +
                   (ShowsMagnifierCoordinateShortcut && _magnifierCoordinateShortcutText is not null ? 1 : 0);

            static FormattedText? ShortcutHint(SelectionCommandDefinition? command)
            {
                return command is null || string.IsNullOrEmpty(command.Gesture)
                    ? null
                    : HintText($"{command.Gesture}: {UiText.Get(command.DescriptionKey)}", MagnifierWidth - 16,
                        MutedText, 11, 20);
            }
        }

        private void DrawMagnifierAnnotations(DrawingContext context, Point center, double factor, PixelPoint point)
        {
            if (MagnifierAnnotations is not { } annotations) return;
            using var transform = context.PushTransform(new(factor, 0, 0, factor,
                center.X - (point.X + .5) * factor, center.Y - (point.Y + .5) * factor));
            if (CapturePreferences.MagnifierAnnotations) annotations.Render(context);
            if (CapturePreferences.MagnifierAnnotationControls) annotations.RenderSelection(context, Scale);
        }

        private bool TryGetSourceSampleColor(PixelPoint point, out Color color)
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
