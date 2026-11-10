using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private readonly Dictionary<string, AnnotationToolStyle> _toolStyles = [];
        private bool _limitToSelection = true;
        private Color _masterColor = Color.FromRgb(216, 74, 47);
        private bool _sharedColor;
        private ScreenshotTool _styleTool;

        public Func<ToolbarPreferences>? ToolbarPreferencesProvider { get; set; }
        internal object ToolbarPreferencesIdentity => ToolbarPreferencesProvider?.Invoke() ?? _behavior.Toolbar;

        public ToolbarPreferences ToolbarPreferences =>
            (ToolbarPreferencesProvider?.Invoke() ?? _behavior.Toolbar).Normalize();

        public long ToolbarRevision { get; private set; }

        private PixelRect AnnotationBounds
        {
            get
            {
                if (_limitToSelection || _frames.Count == 0) return Selection;
                var left = Math.Min(Selection.X, _frames.Min(frame => frame.Region.X));
                var top = Math.Min(Selection.Y, _frames.Min(frame => frame.Region.Y));
                var right = Math.Max(Selection.Right, _frames.Max(frame => frame.Region.Right));
                var bottom = Math.Max(Selection.Bottom, _frames.Max(frame => frame.Region.Bottom));
                return new(left, top, right - left, bottom - top);
            }
        }

        public event Action<ToolbarPreferences, bool?>? ToolbarPreferencesChanged;

        public void UpdateToolbarPreferences(ToolbarPreferences preferences, bool? tools = null)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            if (_disposed) return;
            var toolbar = ToolbarPreferences.Merge(preferences, tools);
            _behavior = _behavior with { Toolbar = toolbar };
            ToolbarRevision++;
            ToolbarPreferencesChanged?.Invoke(toolbar.Normalize(), tools);
            Changed?.Invoke();
        }

        private AnnotationToolStyle StyleFor(ScreenshotTool tool)
        {
            var definition = ScreenshotToolCatalog.Get(tool);
            return _toolStyles.GetValueOrDefault(definition.StyleKey, definition.DefaultStyle);
        }

        private void StoreToolStyle(ScreenshotTool tool)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (tool)
            {
                case ScreenshotTool.Select: return;
                case ScreenshotTool.Watermark:
                    Properties = Properties with { WatermarkText = Text, WatermarkImagePath = _watermarkSourcePath };
                    break;
            }

            _toolStyles[ScreenshotToolCatalog.Get(tool).StyleKey] = new()
            {
                Color = Color.ToString(), Thickness = Thickness, FontSize = FontSize, MosaicSize = MosaicSize,
                Properties = tool == ScreenshotTool.Number ? Properties with { Number = NextNumber } : Properties,
            };
        }

        private void RestoreToolStyle(ScreenshotTool tool)
        {
            var style = StyleFor(tool);
            _styleTool = tool;
            Color = _sharedColor && tool != ScreenshotTool.Highlight ? _masterColor : Color.Parse(style.Color);
            Thickness = tool == ScreenshotTool.Brush
                ? Math.Min(style.Thickness, MaximumBrushThickness)
                : style.Thickness;
            FontSize = style.FontSize;
            MosaicSize = style.MosaicSize;
            Properties = tool == ScreenshotTool.Ellipse
                ? style.Properties with { GeometryShape = AnnotationGeometryShape.Ellipse }
                : style.Properties;
            if (tool == ScreenshotTool.AutoMosaic && Properties.MosaicMode == AnnotationMosaicMode.SmartErase)
                Properties = Properties with { MosaicMode = AnnotationMosaicMode.Mosaic };
            RestoreWatermarkStyle(tool);
        }

        private void LoadObjectStyle(ScreenshotAnnotation annotation)
        {
            StoreToolStyle(_styleTool);
            _styleTool = AnnotationTool(annotation);
            Properties = annotation.Properties;
            Color = annotation.Color;
            Thickness = annotation.Thickness;
            FontSize = annotation.FontSize;
            MosaicSize = annotation.MosaicSize;
            LoadWatermarkObjectStyle(annotation);
        }

        public void ApplyPreferences(AnnotationPreferences preferences)
        {
            ApplyPreferencesCore(preferences);
        }

        private void ApplyPreferencesCore(AnnotationPreferences preferences)
        {
            var value = preferences.Normalize();
            _behavior = value;
            _secondaryEditingOverride = null;
            ToolbarRevision++;
            ColorPalette = [.. value.ColorPalette.Select(Color.Parse)];
            _masterColor = Color.Parse(value.Color);
            _limitToSelection = value.LimitToSelection;
            _sharedColor = value.SharedColor;
            _selected = null;
            _toolStyles.Clear();
            foreach (var definition in ScreenshotToolCatalog.All)
            {
                if (definition.Tool == ScreenshotTool.Select || _toolStyles.ContainsKey(definition.StyleKey)) continue;
                var style = value.ToolStyles.GetValueOrDefault(definition.StyleKey, definition.DefaultStyle);
                if (definition.Tool == ScreenshotTool.Number && value.NumberStartsAtZero &&
                    !value.ToolStyles.ContainsKey(definition.StyleKey))
                    style = style with { Properties = style.Properties with { Number = 0 } };
                _toolStyles.Add(definition.StyleKey, style);
            }

            _tool = value.RememberLastTool ? Enum.Parse<ScreenshotTool>(value.LastTool) : ScreenshotTool.Select;
            if (!CanUseDrawingTool(_tool)) _tool = ScreenshotTool.Select;
            RestoreToolStyle(_tool);
            _nextNumber = StyleFor(ScreenshotTool.Number).Properties.Number;
            Changed?.Invoke();
        }

        public AnnotationPreferences CapturePreferences(AnnotationPreferences preferences)
        {
            var value = preferences.Normalize();
            StoreToolStyle(_styleTool);
            return value with
            {
                LastTool = value.RememberLastTool ? Tool.ToString() : value.LastTool,
                Color = _masterColor.ToString(), ToolStyles = new(_toolStyles),
            };
        }
    }
}
