using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecognitionWindow
    {
        private readonly List<RecognitionTextRange> _textWordRanges = [];
        private readonly List<Rect> _textHighlightBounds = [];
        private ImageTextSelectionDocument _originalTextDocument = ImageTextSelectionDocument.Empty;
        private OcrRecognitionResult? _documentResult;
        private bool _documentRightToLeft;
        private Task _copyTask = Task.CompletedTask;
        private Task _closingCopyTask = Task.CompletedTask;
        private char[] _textRangeBuffer = [];

        private RecognitionPreferences TextFormattingPreferences => _preferences with
        {
            Layout = _dialogOptions.Layout ?? _preferences.Layout,
            Punctuation = _dialogOptions.Punctuation ?? _preferences.Punctuation,
        };

        private void ApplyTextFormatting()
        {
            var text = _recognizedPlainText;
            if (_recognizedResult is { Lines.Count: > 1 } result &&
                result.Lines.All(line => line.Bounds.Height > line.Bounds.Width))
                text = string.Join(Environment.NewLine, OrderedTextLines(result).Select(line => line.Text));
            text = RecognitionTextFormatter.Format(text, TextFormattingPreferences);
            if (_dialogOptions.RemoveSpaces)
            {
                var output = new StringBuilder(text.Length);
                foreach (var character in text)
                    if (!char.IsWhiteSpace(character) || character is '\r' or '\n')
                        output.Append(character);
                text = output.ToString();
            }

            _text.Text = text;
            RefreshTextWordRanges();
            TextSelectionChanged();
        }

        private void TextSelectionChanged()
        {
            if (_mode != RecognitionMode.Text) return;
            _speechControls?.Refresh();
            if (_originalSurface is not { } surface) return;
            if (!_dialogOptions.HighlightSelectedText || _recognizedResult is null ||
                _text.SelectionStart == _text.SelectionEnd)
            {
                surface.SetHighlights([]);
                return;
            }

            var selectedStart = Math.Min(_text.SelectionStart, _text.SelectionEnd);
            var selectedEnd = Math.Max(_text.SelectionStart, _text.SelectionEnd);
            _textHighlightBounds.Clear();
            foreach (var range in _textWordRanges)
                if (range.Start < selectedEnd && range.End > selectedStart)
                    _textHighlightBounds.Add(range.Bounds);
            surface.SetHighlights(_textHighlightBounds);
        }

        private IEnumerable<OcrLine> OrderedTextLines(OcrRecognitionResult result)
        {
            if (!result.Lines.All(line => line.Bounds.Height > line.Bounds.Width)) return result.Lines;
            return _dialogOptions.VerticalRightToLeft
                ? result.Lines.OrderByDescending(line => line.Bounds.X)
                : result.Lines.OrderBy(line => line.Bounds.X);
        }

        private void RefreshTextWordRanges()
        {
            _textWordRanges.Clear();
            EnsureOriginalTextDocument();
            var text = _text.Text ?? "";
            var cursor = 0;
            var punctuation = _dialogOptions.Punctuation ?? _preferences.Punctuation;
            if (_textRangeBuffer.Length < _originalTextDocument.Text.Length)
                _textRangeBuffer = new char[_originalTextDocument.Text.Length];
            foreach (var span in _originalTextDocument.Spans)
            {
                var length = 0;
                foreach (var character in _originalTextDocument.Text.AsSpan(span.Start, span.Length))
                {
                    if (_dialogOptions.RemoveSpaces && char.IsWhiteSpace(character)) continue;
                    _textRangeBuffer[length++] = RecognitionTextFormatter.ConvertPunctuation(character, punctuation);
                }

                if (length == 0) continue;
                var position = text.AsSpan(cursor)
                    .IndexOf(_textRangeBuffer.AsSpan(0, length), StringComparison.Ordinal);
                if (position < 0) continue;
                position += cursor;
                cursor = position + length;
                _textWordRanges.Add(new(span.Bounds, position, cursor));
            }
        }

        private void EnsureOriginalTextDocument()
        {
            if (ReferenceEquals(_documentResult, _recognizedResult) &&
                _documentRightToLeft == _dialogOptions.VerticalRightToLeft) return;
            _documentResult = _recognizedResult;
            _documentRightToLeft = _dialogOptions.VerticalRightToLeft;
            _originalTextDocument = _recognizedResult is { } result
                ? ImageTextSelectionBuilder.FromOcr(result with { Lines = [.. OrderedTextLines(result)] })
                : ImageTextSelectionDocument.Empty;
            if (_originalSurface is { } surface) surface.Document = _originalTextDocument;
        }

        private string SelectedOrAllText()
        {
            return _text.SelectionStart == _text.SelectionEnd ? _text.Text ?? "" : _text.SelectedText;
        }

        private async Task CopyTextAsync(bool close, bool autoClose = true)
        {
            var text = SelectedOrAllText();
            if (string.IsNullOrEmpty(text)) return;
            var selected = _text.SelectionStart != _text.SelectionEnd;
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
            _copyCompleted = true;
            if (!_lifetime.IsCancellationRequested && (close || (autoClose &&
                                                                 (selected
                                                                     ? _dialogOptions.CloseAfterCopySelected
                                                                     : _dialogOptions.CloseAfterCopyAll))))
                Close();
        }

        private void ClosingTextRecognition(object? sender, WindowClosingEventArgs args)
        {
            if (_closingCopy || _copyCompleted || !_dialogOptions.CopyTextOnClose ||
                _lifetime.IsCancellationRequested || string.IsNullOrEmpty(_text.Text)) return;
            args.Cancel = true;
            _closingCopy = true;
            _closingCopyTask = CopyAndCloseTextAsync();
        }

        private async Task CopyAndCloseTextAsync()
        {
            try
            {
                await CopyTextAsync(false, false);
            }
            catch (Exception error)
            {
                LogHub.GetLogger("Desktop.Recognition")
                    .ZLogWarning(error, $"Text copy during recognition close failed.");
            }
            finally
            {
                _copyCompleted = true;
                _closingCopy = false;
                Close();
            }
        }

        private void TextRecognitionShortcut(object? sender, KeyEventArgs args)
        {
            if (args.Handled) return;
            var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ??
                          (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (args.Key)
            {
                case Key.W when args.KeyModifiers == command:
                case Key.Escape when args.KeyModifiers == KeyModifiers.None:
                    args.Handled = true;
                    Close();
                    break;
                case Key.C when args.KeyModifiers == command && !_busy &&
                                _originalSurface?.IsKeyboardFocusWithin != true:
                    args.Handled = true;
                    if (_copyTask.IsCompleted) _copyTask = CopyTextCommandAsync();
                    break;
            }
        }

        private async Task CopyTextCommandAsync()
        {
            try
            {
                await CopyTextAsync(false);
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
        }

        private async Task CopyButtonAsync()
        {
            try
            {
                if (_mode == RecognitionMode.Text)
                {
                    await CopyTextAsync(true);
                }
                else
                {
                    var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    await clipboard.SetTextAsync(_text.Text ?? "");
                    await clipboard.FlushAsync();
                }
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
                LogHub.GetLogger("Desktop.Recognition").ZLogWarning(error, $"Recognition text copy failed.");
            }
        }

        private async Task CopyOriginalTextAsync(string text)
        {
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
        }
    }
}
