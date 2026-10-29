using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationPropertyPresenter(
        ScreenshotAnnotationSession session,
        Func<Task> loadWatermark,
        Func<IDisposable>? dialogScopeProvider = null)
        : ContentControl, IDisposable
    {
        private bool _disposed;
        private ScreenshotTool? _tool;
        private AnnotationPropertyView? _view;

        public bool HasContext => _view is not null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _view?.Dispose();
            _view = null;
            Content = null;
        }

        public void Refresh(ScreenshotTool tool)
        {
            if (_disposed) return;
            if (_tool != tool)
            {
                _view?.Dispose();
                var definition = ScreenshotToolCatalog.Get(tool);
                _view = definition.Properties.Count == 0
                    ? null
                    : new(session, definition, loadWatermark, dialogScopeProvider);
                _tool = tool;
                Content = _view;
            }

            _view?.Refresh();
        }

        public void Reset()
        {
            var current = _tool;
            _view?.Dispose();
            _view = null;
            _tool = null;
            Content = null;
            if (current is { } tool) Refresh(tool);
        }

        public void EditComment()
        {
            Refresh(ScreenshotTool.Arrow);
            _view?.EditComment();
        }
    }
}
