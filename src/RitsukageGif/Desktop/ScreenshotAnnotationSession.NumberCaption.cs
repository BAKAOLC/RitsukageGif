using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private bool _numberCaptionKeyHeld;
        private ScreenshotAnnotation? _numberCaptionPreview;

        internal bool IsNumberCaptionEditing => IsCanvasTextEditing && _textEditOriginal?.Tool == ScreenshotTool.Number;

        public bool HandleNumberCaptionKey(Key key, KeyModifiers modifiers, bool down)
        {
            if (key != Key.A) return false;
            if (!down)
            {
                if (!_numberCaptionKeyHeld) return false;
                _numberCaptionKeyHeld = false;
                if (modifiers == KeyModifiers.None) BeginNumberCaptionEdit();
                return true;
            }

            if (modifiers != KeyModifiers.None || !CanBeginNumberCaptionEdit()) return false;
            _numberCaptionKeyHeld = true;
            return true;
        }

        public void ReleaseNumberCaptionKey()
        {
            _numberCaptionKeyHeld = false;
        }

        private bool CanBeginNumberCaptionEdit()
        {
            return !_disposed && !IsBusy && !IsDrawing && !IsEditingText &&
                   _selected is { Tool: ScreenshotTool.Number } target && CanEditAnnotation(target, true);
        }

        private void BeginNumberCaptionEdit()
        {
            if (!CanBeginNumberCaptionEdit()) return;
            var target = _selected!;
            StartTextEdit(target, NumberCaptionOrigin(target), true, false);
        }

        private static Point NumberCaptionOrigin(ScreenshotAnnotation annotation)
        {
            var line = new FormattedText(string.IsNullOrEmpty(annotation.Text) ? " " : annotation.Text,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                CreateTypeface(annotation.Properties),
                NumberCaptionFontSize(annotation.Thickness, annotation.Properties),
                Brushes.Black);
            return NumberCaptionAnchor(annotation) - new Vector(0, line.Height / 2);
        }

        private static Point NumberCaptionAnchor(ScreenshotAnnotation annotation)
        {
            return annotation.Points.Count > 2
                ? annotation.Points[2]
                : annotation.Points[0] + new Vector(annotation.Thickness * Math.Sqrt(2) * 1.2, 0);
        }

        private void UpdateNumberCaptionEditorPosition()
        {
            if (!IsNumberCaptionEditing) return;
            var annotation = NumberCaptionRenderingAnnotation(_textEditOriginal!);
            var text = new FormattedText(string.IsNullOrEmpty(TextEditValue) ? " " : TextEditValue,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                CreateTypeface(Properties), NumberCaptionFontSize(Thickness, Properties), Brushes.Black);
            TextEditPosition = NumberCaptionAnchor(annotation) - new Vector(0, text.Height / 2);
        }

        private static double NumberCaptionFontSize(double size, ScreenshotAnnotationProperties properties)
        {
            return Math.Max(1, (int)(size * 3 * properties.NumberCaptionScale));
        }

        private ScreenshotAnnotation NumberCaptionRenderingAnnotation(ScreenshotAnnotation annotation)
        {
            if (!IsNumberCaptionEditing || !ReferenceEquals(annotation, _textEditOriginal)) return annotation;
            return _numberCaptionPreview ??= CloneAnnotation(annotation, text: "", color: Color, thickness: Thickness,
                properties: Properties);
        }

        private void ClearNumberCaptionPreview()
        {
            _numberCaptionPreview?.Dispose();
            _numberCaptionPreview = null;
        }
    }
}
