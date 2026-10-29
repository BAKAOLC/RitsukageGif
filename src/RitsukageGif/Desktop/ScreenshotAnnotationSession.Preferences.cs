using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private readonly Dictionary<string, AnnotationToolStyle> _toolStyles = [];
        private AnnotationToolStyle _defaultStyle = new();
        private bool _limitToSelection = true;
        private bool _sharedColor = true;

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

        private void StoreToolStyle(ScreenshotTool tool)
        {
            _toolStyles[tool.ToString()] = new()
            {
                Color = Color.ToString(), Thickness = Thickness, FontSize = FontSize, MosaicSize = MosaicSize,
                Properties = Properties,
            };
        }

        private void RestoreToolStyle(ScreenshotTool tool)
        {
            var style = _toolStyles.GetValueOrDefault(tool.ToString(), _defaultStyle);
            if (!_sharedColor) Color = Color.Parse(style.Color);
            Thickness = tool == ScreenshotTool.Brush
                ? Math.Min(style.Thickness, MaximumBrushThickness)
                : style.Thickness;
            FontSize = style.FontSize;
            MosaicSize = style.MosaicSize;
            Properties = style.Properties;
        }

        public void ApplyPreferences(AnnotationPreferences preferences)
        {
            ApplyPreferencesCore(preferences);
        }

        private void ApplyPreferencesCore(AnnotationPreferences preferences)
        {
            var value = preferences.Normalize();
            _behavior = value;
            ToolbarRevision++;
            ColorPalette = [.. value.ColorPalette.Select(Color.Parse)];
            Color = Color.Parse(value.Color);
            Thickness = value.Thickness;
            FontSize = value.FontSize;
            MosaicSize = value.MosaicSize;
            _limitToSelection = value.LimitToSelection;
            _sharedColor = value.SharedColor;
            _defaultStyle = new()
            {
                Color = value.Color, Thickness = value.Thickness, FontSize = value.FontSize,
                MosaicSize = value.MosaicSize, Properties = value.Properties,
            };
            Tool = value.RememberLastTool ? Enum.Parse<ScreenshotTool>(value.LastTool) : ScreenshotTool.Select;
            _toolStyles.Clear();
            foreach (var pair in value.ToolStyles) _toolStyles.Add(pair.Key, pair.Value);
            if (value.NumberStartsAtZero && !value.ToolStyles.ContainsKey(nameof(ScreenshotTool.Number)))
                _toolStyles[nameof(ScreenshotTool.Number)] =
                    _defaultStyle with { Properties = value.Properties with { Number = 0 } };
            RestoreToolStyle(Tool);
            Changed?.Invoke();
        }

        public AnnotationPreferences CapturePreferences(AnnotationPreferences preferences)
        {
            var value = preferences.Normalize();
            StoreToolStyle(Tool);
            return value with
            {
                LastTool = value.RememberLastTool ? Tool.ToString() : value.LastTool,
                Color = Color.ToString(), Thickness = Thickness, FontSize = FontSize, MosaicSize = MosaicSize,
                Properties = Properties, ToolStyles = new(_toolStyles),
            };
        }
    }
}
