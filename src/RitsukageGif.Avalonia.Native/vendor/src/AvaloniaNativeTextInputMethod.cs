using System;
using Avalonia.Input.TextInput;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class AvaloniaNativeTextInputMethod : ITextInputMethodImpl, IDisposable
    {
        private TextInputMethodClient? _client;
        private AvnTextInputMethodClient? _nativeClient;
        private readonly IAvnTextInputMethod _inputMethod;

        // ReSharper disable once ConvertToPrimaryConstructor
        public AvaloniaNativeTextInputMethod(IAvnTopLevel topLevel)
        {
            _inputMethod = topLevel.InputMethod;
        }

        public void Dispose()
        {
            _inputMethod.Dispose();
            _nativeClient?.Dispose();
        }

        public void Reset()
        {
            _inputMethod.Reset();
        }

        public void SetClient(TextInputMethodClient? client)
        {
            if (_client is { SupportsSurroundingText: true })
            {
                _client.SurroundingTextChanged -= OnSurroundingTextChanged;
                _client.CursorRectangleChanged -= OnCursorRectangleChanged;
                _client.SelectionChanged -= OnSelectionChanged;

                _nativeClient?.Dispose();
            }

            _nativeClient = null;
            _client = client;

            if (_client != null)
            {
                _nativeClient = new(_client);

                OnSurroundingTextChanged(this, EventArgs.Empty);
                OnCursorRectangleChanged(this, EventArgs.Empty);
                // Note: OnSelectionChanged isn't called, it's already up-to-date thanks to OnSurroundingTextChanged

                _client.SurroundingTextChanged += OnSurroundingTextChanged;
                _client.CursorRectangleChanged += OnCursorRectangleChanged;
                _client.SelectionChanged += OnSelectionChanged;
            }

            _inputMethod.SetClient(_nativeClient);
        }

        public void SetCursorRect(Rect rect)
        {
            _inputMethod.SetCursorRect(rect.ToAvnRect());
        }

        public void SetOptions(TextInputOptions options)
        {
        }

        private void OnCursorRectangleChanged(object? sender, EventArgs e)
        {
            var textViewVisual = _client?.TextViewVisual;
            var visualRoot = textViewVisual?.VisualRoot;

            if (visualRoot is null) return;

            var transform = textViewVisual!.TransformToVisual(visualRoot);

            if (transform == null) return;

            var rect = _client!.CursorRectangle.TransformToAABB(transform.Value);

            _inputMethod.SetCursorRect(rect.ToAvnRect());
        }

        private void OnSurroundingTextChanged(object? sender, EventArgs e)
        {
            if (_client == null) return;

            var surroundingText = _client.SurroundingText;
            var selection = _client.Selection;

            _inputMethod.SetSurroundingText(
                surroundingText,
                selection.Start,
                selection.End
            );
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            if (_client is null) return;

            var selection = _client.Selection;
            _inputMethod.SetSelectionInSurroundingText(selection.Start, selection.End);
        }

        private class AvnTextInputMethodClient : NativeCallbackBase, IAvnTextInputMethodClient
        {
            private readonly TextInputMethodClient _client;

            // ReSharper disable once ConvertToPrimaryConstructor
            public AvnTextInputMethodClient(TextInputMethodClient client)
            {
                _client = client;
            }

            public void SetPreeditText(string preeditText)
            {
                if (_client.SupportsPreedit) _client.SetPreeditText(preeditText);
            }

            public void SelectInSurroundingText(int start, int end)
            {
                if (_client.SupportsSurroundingText) _client.Selection = new(start, end);
            }
        }
    }
}
