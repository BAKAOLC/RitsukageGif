using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private bool _textEditDetached, _textEditCreatesAnnotation, _textEditWatermarkText;
        private int _textEditIndex;
        private ScreenshotAnnotation? _textEditOriginal;
        private AnnotationToolStyle? _textEditPreviousStyle;
        private string _textEditPreviousText = "";

        public bool IsEditingText { get; private set; }
        public bool IsCanvasTextEditing { get; private set; }
        public long TextEditGeneration { get; private set; }
        public Point TextEditPosition { get; private set; }
        public string TextEditValue { get; private set; } = "";
        public bool TextEditWraps => _behavior.TextWrapMode != AnnotationTextWrapMode.NoWrap;

        public double TextEditWrapWidth => Properties.WrapWidth > 0
            ? Properties.WrapWidth
            : Math.Max(1, AnnotationBounds.Right - TextEditPosition.X - Properties.TextPadding * 2);

        public event Action? TextEditStateChanged;

        public bool BeginTextEdit(PixelPoint point, bool existingOnly = false)
        {
            if (_disposed || !CanAnnotate || IsBusy || !AnnotationBounds.Contains(point)) return false;
            CommitTextEdit();
            var target = FindAnnotation(point) is { Tool: ScreenshotTool.Text } found ? found : null;
            if (target is null && (existingOnly || Tool != ScreenshotTool.Text || _annotations.Count >= 1024))
                return false;
            StartTextEdit(target, target?.Points[0] ?? new(point.X, point.Y), true, false);
            return true;
        }

        public bool BeginTextPropertyEdit()
        {
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing) return false;
            CommitTextEdit();
            var target = _selected is { Tool: ScreenshotTool.Arrow or ScreenshotTool.Watermark } value ? value : null;
            if (target is null && Tool != ScreenshotTool.Watermark) return false;
            StartTextEdit(target, target?.Points[0] ?? default, false,
                target?.Tool == ScreenshotTool.Watermark || target is null);
            return true;
        }

        private void StartTextEdit(ScreenshotAnnotation? target, Point position, bool canvas, bool watermarkText)
        {
            CancelGesture();
            if (target is not null)
            {
                Properties = target.Properties;
                Color = target.Color;
                Thickness = target.Thickness;
                FontSize = target.FontSize;
                MosaicSize = target.MosaicSize;
            }

            _textEditPreviousStyle = new()
            {
                Color = Color.ToString(), Thickness = Thickness, FontSize = FontSize,
                MosaicSize = MosaicSize, Properties = Properties.Normalize(),
            };
            _textEditPreviousText = Text;
            _textEditOriginal = target;
            _textEditIndex = target is null ? _annotations.Count : _annotations.IndexOf(target);
            _textEditDetached = canvas && target is not null;
            _textEditCreatesAnnotation = canvas && target is null;
            _textEditWatermarkText = watermarkText;
            if (_textEditDetached) _annotations.RemoveAt(_textEditIndex);
            _selected = target;
            TextEditPosition = position;
            TextEditValue = target?.Text ?? (canvas ? "" : Text);
            IsEditingText = true;
            IsCanvasTextEditing = canvas;
            TextEditGeneration++;
            NotifyTextEditState();
        }

        public void UpdateTextEditDraft(string value)
        {
            if (!IsEditingText || TextEditValue == value) return;
            TextEditValue = value;
            TextEditStateChanged?.Invoke();
        }

        public void CommitTextEdit()
        {
            if (!IsEditingText) return;
            var original = _textEditOriginal;
            var index = Math.Clamp(_textEditIndex, 0, _annotations.Count);
            if (_textEditDetached && original is not null) _annotations.Insert(index, original);
            IsEditingText = false;
            IsCanvasTextEditing = false;
            TextEditGeneration++;
            _textEditOriginal = null;
            _textEditPreviousStyle = null;
            Text = TextEditValue;
            if (original is not null)
            {
                if (original.Tool == ScreenshotTool.Text && string.IsNullOrWhiteSpace(TextEditValue))
                {
                    SaveHistory();
                    _annotations.RemoveAt(index);
                    _selected = null;
                }
                else if (original.Text != TextEditValue || original.Color != Color ||
                         !original.FontSize.Equals(FontSize) || original.Properties != Properties ||
                         (_textEditWatermarkText && original.WatermarkImage is not null))
                {
                    var replacement = CloneAnnotation(original, text: TextEditValue, color: Color,
                        fontSize: FontSize, properties: Properties, replaceImage: _textEditWatermarkText);
                    SaveHistory();
                    _retained.Add(replacement);
                    _annotations[index] = replacement;
                    _selected = replacement;
                }
            }
            else if (_textEditCreatesAnnotation && !string.IsNullOrWhiteSpace(TextEditValue))
            {
                var annotation = new ScreenshotAnnotation
                {
                    Tool = ScreenshotTool.Text, Text = TextEditValue, Color = Color, Thickness = Thickness,
                    FontSize = FontSize, MosaicSize = MosaicSize, Properties = Properties.Normalize(),
                };
                annotation.Points.Add(new(TextEditPosition.X, TextEditPosition.Y));
                Commit(annotation);
                _selected = annotation;
            }

            if (_textEditWatermarkText)
            {
                _watermarkSource?.Dispose();
                _watermarkSource = null;
            }

            NotifyTextEditState();
        }

        public void CancelTextEdit()
        {
            if (!IsEditingText) return;
            if (_textEditOriginal is { } original)
            {
                if (_textEditDetached) _annotations.Insert(Math.Clamp(_textEditIndex, 0, _annotations.Count), original);
                _selected = original;
            }
            else
            {
                _selected = null;
            }

            if (_textEditPreviousStyle is { } previous)
            {
                Color = Color.Parse(previous.Color);
                Thickness = previous.Thickness;
                FontSize = previous.FontSize;
                MosaicSize = previous.MosaicSize;
                Properties = previous.Properties;
            }

            Text = _textEditPreviousText;
            TextEditValue = Text;
            IsEditingText = false;
            IsCanvasTextEditing = false;
            TextEditGeneration++;
            _textEditOriginal = null;
            _textEditPreviousStyle = null;
            NotifyTextEditState();
        }

        private void NotifyTextEditState()
        {
            TextEditStateChanged?.Invoke();
            Changed?.Invoke();
        }
    }
}
