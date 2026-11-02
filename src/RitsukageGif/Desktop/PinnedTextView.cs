using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedTextView : TextEditor, IPinnedDocumentTextView
    {
        private readonly int[] _breaks;
        private readonly string _original;

        public PinnedTextView(string text, InlineCollection? inlines = null)
        {
            _original = text;
            var display = new StringBuilder(text.Length);
            var breaks = new List<int>();
            var column = 0;
            for (var index = 0; index < text.Length;)
            {
                var length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
                if (column >= 1024 && text[index] is not '\r' and not '\n')
                {
                    breaks.Add(index);
                    display.Append('\n');
                    column = 0;
                }

                display.Append(text, index, length);
                column = text[index] is '\r' or '\n' ? 0 : column + length;
                index += length;
            }

            _breaks = [.. breaks];
            Text = display.ToString();
            IsReadOnly = true;
            TextArea.DefaultInputHandler.Editing.KeyBindings.Clear();
            TextArea.DefaultInputHandler.Editing.CommandBindings.Clear();
            WordWrap = true;
            FontFamily = FontFamily.Default;
            FontSize = 14;
            Background = Brushes.Transparent;
            ShowLineNumbers = false;
            BorderThickness = new(0);
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (inlines is not null)
            {
                var ranges = new List<PinnedTextStyleRange>();
                var offset = 0;
                AddStyles(inlines, new(FontFamily.Default), null, null, 14, null,
                    ranges, ref offset);
                TextArea.TextView.LineTransformers.Add(new PinnedTextColorizer(ranges));
            }
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public Control Control => this;
        public string? SelectedHtml => null;

        public Vector ScrollOffset
        {
            get => new(HorizontalOffset, VerticalOffset);
            set => TextEditorScrolling.Restore(this, value);
        }

        public event EventHandler? ScrollOffsetChanged
        {
            add => TextArea.TextView.ScrollOffsetChanged += value;
            remove => TextArea.TextView.ScrollOffsetChanged -= value;
        }

        public new string SelectedText
        {
            get
            {
                var start = ToOriginal(SelectionStart);
                var end = ToOriginal(SelectionStart + SelectionLength);
                return _original[start..end];
            }
        }

        public void SetSelectable(bool value)
        {
            var handler = TextArea.DefaultInputHandler;
            if (value)
            {
                if (!handler.NestedInputHandlers.Contains(handler.MouseSelection))
                    handler.NestedInputHandlers.Add(handler.MouseSelection);
            }
            else
            {
                handler.NestedInputHandlers.Remove(handler.MouseSelection);
                ClearSelection();
            }
        }

        public void ReleaseContent()
        {
            TextArea.TextView.LineTransformers.Clear();
            Document = null;
        }

        private void ClearSelection()
        {
            Select(SelectionStart, 0);
        }

        private int ToOriginal(int offset)
        {
            var low = 0;
            var high = _breaks.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_breaks[middle] + middle < offset) low = middle + 1;
                else high = middle;
            }

            return Math.Clamp(offset - low, 0, _original.Length);
        }

        private int ToDisplay(int offset)
        {
            var index = Array.BinarySearch(_breaks, offset);
            return offset + (index >= 0 ? index + 1 : ~index);
        }

        private void AddStyles(InlineCollection inlines, Typeface typeface, IBrush? foreground, IBrush? background,
            double size, TextDecorationCollection? decorations, List<PinnedTextStyleRange> ranges, ref int offset)
        {
            foreach (var inline in inlines)
            {
                var face = new Typeface(
                    inline.IsSet(TextElement.FontFamilyProperty) ? inline.FontFamily : typeface.FontFamily,
                    inline.IsSet(TextElement.FontStyleProperty) ? inline.FontStyle : typeface.Style,
                    inline.IsSet(TextElement.FontWeightProperty) ? inline.FontWeight : typeface.Weight);
                var front = inline.IsSet(TextElement.ForegroundProperty) ? inline.Foreground : foreground;
                var back = inline.IsSet(TextElement.BackgroundProperty) ? inline.Background : background;
                var fontSize = inline.IsSet(TextElement.FontSizeProperty) ? inline.FontSize : size;
                var decoration = inline.IsSet(Inline.TextDecorationsProperty) ? inline.TextDecorations : decorations;
                switch (inline)
                {
                    case Span span:
                        AddStyles(span.Inlines, face, front, back, fontSize, decoration, ranges, ref offset);
                        break;
                    case Run run:
                        var length = run.Text?.Length ?? 0;
                        if (length > 0)
                            ranges.Add(new(ToDisplay(offset), ToDisplay(offset + length), face, front, back,
                                fontSize, decoration));
                        offset += length;
                        break;
                    case LineBreak:
                        offset++;
                        break;
                }
            }
        }
    }
}
