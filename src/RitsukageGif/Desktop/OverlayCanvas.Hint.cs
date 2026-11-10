using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private const double HintMargin = 8;
        private const double HintPadding = 10;

        private static readonly Typeface HintTypeface =
            new(new FontFamily("Segoe UI, Microsoft YaHei UI, Noto Sans, DejaVu Sans"));

        private static readonly IBrush HintBackground = new SolidColorBrush(Color.FromArgb(250, 30, 32, 36));
        private static readonly IPen HintBorder = new Pen(new SolidColorBrush(Color.FromRgb(104, 108, 116)));
        private static readonly IBrush MutedText = new SolidColorBrush(Color.FromRgb(204, 207, 212));
        private readonly List<(FormattedText Key, FormattedText Label, Point Origin)> _actionChips = [];

        private (SelectionPhase Phase, bool RegionEmpty, bool CanConfirm, bool Controls, bool Source, bool Cursor,
            bool Full, bool Screenshot, bool Drawing, bool Editing, SelectionHintMode HintMode, int SnapMode,
            double Width, double Height, string UiCulture, string Culture,
            IReadOnlyList<SelectionCommandDefinition>? Bindings) _actionsKey;

        private Size _actionsSize;

        private (PixelRect Region, double Width, bool Measuring, string UiCulture, string Culture, string Unit)
            _geometryKey;

        private Rect _hintStatusBounds, _hintActionsBounds, _hintMagnifierBounds;

        private (string? Process, string? Title, bool WindowSource, bool Follow, SelectionHintMode Mode,
            double Width, string UiCulture, string Culture) _targetKey;

        private FormattedText? _targetText, _captureText, _geometryText;
        internal string? OperationError { get; set; }
        internal string CoordinateUnit { get; set; } = "px";
        internal IReadOnlyList<SelectionCommandDefinition>? ShortcutBindings { get; init; }

        public bool ShowControls { get; init; }
        public bool ShowSource { get; init; }
        public bool ShowCursor { get; set; }
        public bool Controls { get; set; }
        public bool WindowSource { get; set; }
        public SelectionSnapshot? Snapshot { get; set; }
        public SelectionHintMode HintMode { get; set; }
        public bool IsScreenshot { get; set; }
        public bool IsDrawing { get; set; }
        public bool IsEditing { get; set; }
        public Rect ReservedBounds { get; set; }
        public Rect SecondaryReservedBounds { get; set; }
        public Rect FrameReservedBounds { get; set; }
        public Rect ModePickerReservedBounds { get; set; }
        internal Rect InformationReservedBounds { get; set; }
        internal bool InteractiveInformationVisible { get; set; }
        internal bool PointerOverCaptureControls { get; set; }
        internal bool CodePopupVisible { get; set; }
        private bool Measuring => AuxiliaryVisible && CapturePreferences.GuideMeasurements && !Selected.IsEmpty;

        private void DrawHint(DrawingContext context, Point mouse, bool layoutOnly = false)
        {
            _hintStatusBounds = _hintActionsBounds = _hintMagnifierBounds = default;
            var width = Math.Min(280, Bounds.Width - HintMargin * 2);
            var maximumHeight = Bounds.Height - HintMargin * 2;
            if (width < 72 || maximumHeight < 40) return;
            var textWidth = width - HintPadding * 2;
            var actionsWidth = Math.Max(1, Math.Min(320, Bounds.Width - HintMargin * 2) - HintPadding * 2);
            UpdateHintText(textWidth, actionsWidth);

            var local = Selected.ToLocal(_monitor, Scale);
            var selected = Selected.IsEmpty ? default : new Rect(local.X, local.Y, local.Width, local.Height);
            var magnifier = GetMagnifierFrame(mouse);
            var actions = default(Rect);
            if (CapturePreferences.ShowShortcutHints && HintMode != SelectionHintMode.Hidden && _actionChips.Count > 0)
            {
                var candidate = GetActionsFrame(Bounds.Size, _actionsSize, mouse, selected, ReservedBounds,
                    SecondaryReservedBounds, FrameReservedBounds, ModePickerReservedBounds, InformationReservedBounds,
                    magnifier);
                candidate = AvoidCodePanels(candidate, mouse, selected, magnifier);
                if (CodePanelsClear(candidate) && PanelClear(candidate, mouse, selected, ReservedBounds,
                        SecondaryReservedBounds,
                        FrameReservedBounds, ModePickerReservedBounds, InformationReservedBounds, magnifier))
                {
                    actions = candidate;
                    _hintActionsBounds = actions;
                    if (!layoutOnly) DrawActionChips(context, actions);
                }
            }

            var errorText = string.IsNullOrEmpty(OperationError)
                ? null
                : HintText(OperationError, textWidth, Brushes.White, 12, 36);
            if ((CapturePreferences.ShowInformation && !InteractiveInformationVisible && !Selected.IsEmpty) ||
                errorText is not null)
            {
                var showRegion = CapturePreferences.ShowInformation && !InteractiveInformationVisible &&
                                 !Selected.IsEmpty;
                var showTarget = CapturePreferences.ShowInformation && !InteractiveInformationVisible &&
                                 _targetText is not null;
                var targetHeight = showTarget ? Measuring ? 20 : 40 : 0;
                var geometryHeight = showRegion ? 20 : 0;
                var errorHeight = errorText is null ? 0 : errorText.Height + 4;
                var contentWidth = Math.Max(showRegion ? _geometryText!.WidthIncludingTrailingWhitespace : 0,
                    Math.Max(showTarget && !Measuring ? _targetText!.WidthIncludingTrailingWhitespace : 0,
                        Math.Max(showTarget ? _captureText!.WidthIncludingTrailingWhitespace : 0,
                            errorText?.WidthIncludingTrailingWhitespace ?? 0)));
                var statusSize = new Size(Math.Ceiling(contentWidth) + 20,
                    targetHeight + geometryHeight + errorHeight + 12);
                var candidate = GetStatusFrame(Bounds.Size, statusSize, selected, mouse, actions, ReservedBounds,
                    SecondaryReservedBounds, FrameReservedBounds, ModePickerReservedBounds, InformationReservedBounds,
                    magnifier);
                candidate = AvoidCodePanels(candidate, mouse, selected, actions, magnifier);
                if (CodePanelsClear(candidate) && PanelClear(candidate, mouse, selected, actions, ReservedBounds,
                        SecondaryReservedBounds,
                        FrameReservedBounds, ModePickerReservedBounds, InformationReservedBounds, magnifier))
                {
                    _hintStatusBounds = candidate;
                    if (!layoutOnly)
                    {
                        context.DrawRectangle(HintBackground, HintBorder, candidate, 5, 5);
                        using var statusClip = context.PushClip(candidate.Deflate(6));
                        var origin = candidate.TopLeft + new Vector(10, 6);
                        if (showTarget)
                        {
                            if (!Measuring)
                            {
                                context.DrawText(_targetText!, origin);
                                origin += new Vector(0, 20);
                            }

                            context.DrawText(_captureText!, origin);
                            origin += new Vector(0, 20);
                        }

                        if (showRegion)
                        {
                            context.DrawText(_geometryText!, origin);
                            origin += new Vector(0, 20);
                        }

                        if (errorText is not null) context.DrawText(errorText, origin + new Vector(0, 4));
                    }
                }
            }

            if (magnifier is { Width: <= 0 } or { Height: <= 0 }) return;
            _hintMagnifierBounds = magnifier;
            if (layoutOnly) return;
            context.DrawRectangle(HintBackground, HintBorder, magnifier, 6, 6);
            DrawMagnifier(context, new(magnifier.X, magnifier.Y, MagnifierWidth, MagnifierHeight));
            DrawMagnifierDetails(context, magnifier);
        }

        private Rect GetMagnifierFrame(Point mouse)
        {
            var defaultVisible = CapturePreferences.ShowMagnifier &&
                                 (Snapshot?.Phase == SelectionPhase.Dragging ||
                                  Selected.Contains(Mouse));
            if (!(AuxiliaryVisible ? CapturePreferences.GuideMagnifier : defaultVisible) ||
                HintMode == SelectionHintMode.Hidden || IsDrawing || IsEditing || PointerOverCaptureControls ||
                CodePopupVisible)
                return default;
            var lensSize = new Size(MagnifierWidth, MagnifierHeight + 30 +
                                                    (CapturePreferences.MagnifierCoordinates ? 22 : 0) +
                                                    (CapturePreferences.MagnifierShortcuts
                                                        ? UpdateMagnifierShortcuts() * 20 + 4
                                                        : 0));
            return Bounds.Height - HintMargin * 2 < lensSize.Height || Bounds.Width < lensSize.Width + HintMargin * 2
                ? default
                : SelectionMagnifierPlacement.Place(Bounds.Size, lensSize, mouse);
        }

        private void DrawActionChips(DrawingContext context, Rect frame)
        {
            context.DrawRectangle(HintBackground, HintBorder, frame, 6, 6);
            using var clip = context.PushClip(frame.Deflate(HintPadding));
            foreach (var (key, label, origin) in _actionChips)
            {
                var point = frame.TopLeft + new Vector(HintPadding + origin.X, HintPadding + origin.Y);
                var keyWidth = Math.Ceiling(key.WidthIncludingTrailingWhitespace) + 10;
                var keyFrame = new Rect(point, new Size(keyWidth, 22));
                context.DrawRectangle(Brushes.White, null, keyFrame, 3, 3);
                context.DrawText(key, point + new Vector(5, (22 - key.Height) / 2));
                context.DrawText(label, point + new Vector(keyWidth + 12, (22 - label.Height) / 2));
            }
        }

        private void UpdateActionChips(SelectionSnapshot snapshot, bool full, double width)
        {
            ReadOnlySpan<SelectionCommand> commands = (full, snapshot.Phase) switch
            {
                (false, _) =>
                [
                    IsScreenshot ? SelectionCommand.Copy : SelectionCommand.Confirm,
                    SelectionCommand.Cancel, SelectionCommand.CycleHints, SelectionCommand.CopyColor,
                    SelectionCommand.CycleColorFormat,
                ],
                (true, SelectionPhase.Selected) =>
                [
                    IsScreenshot ? SelectionCommand.Copy : SelectionCommand.Confirm,
                    IsScreenshot ? SelectionCommand.Save : SelectionCommand.ToggleSource, SelectionCommand.Move,
                    SelectionCommand.ToggleCursor, SelectionCommand.Clear, SelectionCommand.Cancel,
                    SelectionCommand.CycleHints, SelectionCommand.CopyColor, SelectionCommand.CycleColorFormat,
                ],
                _ =>
                [
                    SelectionCommand.ToggleControls, SelectionCommand.ToggleSource, SelectionCommand.ToggleCursor,
                    SelectionCommand.CycleSnapping, SelectionCommand.Cancel, SelectionCommand.CycleHints,
                    SelectionCommand.CopyColor, SelectionCommand.CycleColorFormat,
                ],
            };
            _actionChips.Clear();
            var keyColumn = 0.0;
            var y = 0.0;
            var usedWidth = 0.0;
            var bindings = ShortcutBindings ?? SelectionCommands.SnapshotBindings();
            foreach (var command in commands)
            {
                if (!SelectionCommands.IsAvailable(command, snapshot, ShowControls, ShowSource, ShowCursor,
                        IsDrawing, IsScreenshot, IsEditing)) continue;
                var matching = bindings.Where(binding => binding.Command == command).ToArray();
                if (matching.Length == 0) continue;
                var gesture = matching[0].Gesture;
                if (command == SelectionCommand.Move && matching.Length == 4 &&
                    matching.All(binding => binding.Key == binding.DirectionKey &&
                                            binding.Modifiers == KeyModifiers.None))
                    gesture = "↑↓←→";
                var key = HintText(gesture, Math.Max(32, width / 2), Brushes.Black, 11, 22);
                var keyWidth = Math.Ceiling(key.WidthIncludingTrailingWhitespace) + 10;
                var label = HintText(UiText.Get(matching[0].DescriptionKey), Math.Max(16, width - keyWidth - 6),
                    MutedText, 12, 22);
                var itemWidth = keyWidth + 12 + Math.Ceiling(label.WidthIncludingTrailingWhitespace);
                if (y + 22 > Math.Max(22, Bounds.Height - 80)) break;
                _actionChips.Add((key, label, new(0, y)));
                keyColumn = Math.Max(keyColumn, keyWidth);
                usedWidth = Math.Max(usedWidth, itemWidth);
                y += 29;
            }

            usedWidth = 0;
            for (var index = 0; index < _actionChips.Count; index++)
            {
                var (key, label, origin) = _actionChips[index];
                var keyWidth = Math.Ceiling(key.WidthIncludingTrailingWhitespace) + 10;
                _actionChips[index] = (key, label, new(keyColumn - keyWidth, origin.Y));
                usedWidth = Math.Max(usedWidth, keyColumn + 12 + label.WidthIncludingTrailingWhitespace);
            }

            _actionsSize = new(Math.Min(width, usedWidth) + HintPadding * 2,
                (_actionChips.Count == 0 ? 0 : y - 7) + HintPadding * 2);
        }

        private void UpdateHintText(double width, double actionsWidth)
        {
            var uiCulture = CultureInfo.CurrentUICulture.Name;
            var culture = CultureInfo.CurrentCulture.Name;
            var phase = Manipulating ? SelectionPhase.Selected : Snapshot?.Phase ?? SelectionPhase.Candidate;
            var region = Selected.IsEmpty ? new(Mouse.X, Mouse.Y, 0, 0) : Selected;
            var geometryKey = (region, width, Measuring, uiCulture, culture, CoordinateUnit);
            if (_geometryText is null || _geometryKey != geometryKey)
            {
                _geometryKey = geometryKey;
                _geometryText =
                    HintText(
                        Measuring
                            ? string.Create(CultureInfo.CurrentCulture, $"{region.X}, {region.Y}")
                            : string.Create(CultureInfo.CurrentCulture,
                                $"{region.X}, {region.Y}   {region.Width} × {region.Height} {CoordinateUnit}"), width,
                        Brushes.White,
                        13,
                        22);
            }

            var target = Snapshot?.Target;
            var targetKey = (target?.Process, target?.Title, WindowSource,
                Snapshot?.FollowsWindow == true && !IsScreenshot, HintMode, width, uiCulture, culture);
            if (_targetKey != targetKey)
            {
                _targetKey = targetKey;
                _targetText = null;
                _captureText = null;
                if (target is not null && HintMode != SelectionHintMode.Hidden)
                {
                    var title = string.IsNullOrWhiteSpace(target.Title) ? target.Process : target.Title;
                    _targetText = HintText(Compact(title), width, Brushes.White, 12, 20);
                    var semantic = UiText.Get(WindowSource ? "SelectionCaptureWindow" : "SelectionCaptureScreen");
                    if (Snapshot?.FollowsWindow == true && !IsScreenshot)
                        semantic = UiText.Get("SelectionCaptureWindowFollowing");
                    _captureText = HintText(semantic, width, MutedText, 12, 20);
                }
            }

            var full = HintMode == SelectionHintMode.Full && Bounds.Width >= 520;
            var actionsHeight = Math.Max(16, Math.Min(100, (Bounds.Height - HintMargin * 2) / 3 - HintPadding * 2));
            var actionsKey = (Snapshot?.Phase ?? phase, Snapshot?.Region.IsEmpty ?? Selected.IsEmpty,
                Snapshot?.CanConfirm ?? !Selected.IsEmpty, ShowControls, ShowSource, ShowCursor,
                full, IsScreenshot, IsDrawing, IsEditing, HintMode, Mode, actionsWidth, actionsHeight, uiCulture,
                culture, ShortcutBindings ?? SelectionCommands.SnapshotBindings());
            if (_actionChips.Count == 0 || _actionsKey != actionsKey)
            {
                _actionsKey = actionsKey;
                var snapshot = Snapshot ?? new SelectionSnapshot(phase, Selected, null, WindowSource, Controls, false,
                    !Selected.IsEmpty);
                UpdateActionChips(snapshot, full, actionsWidth);
            }
        }

        private static string Compact(string value)
        {
            if (value.Length > 160) value = value[..160];
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static FormattedText HintText(string text, double width, IBrush brush, double fontSize, double height)
        {
            return new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HintTypeface, fontSize, brush)
            {
                MaxTextWidth = Math.Max(1, width), MaxTextHeight = height, Trimming = TextTrimming.CharacterEllipsis,
            };
        }
    }
}
