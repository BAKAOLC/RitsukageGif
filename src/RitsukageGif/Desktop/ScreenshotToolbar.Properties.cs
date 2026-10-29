using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<SelectionCommand, Button> _extraCommands = [];
        private readonly Button? _selectToolButton;

        private static string ToolGeometry(ScreenshotTool tool)
        {
            return ToolbarIcon.TryGeometry(ScreenshotToolCatalog.Get(tool).IconKey)
                   ?? ToolbarIcon.Geometry("AnimationSelection");
        }

        private void ArrowCommentRequested()
        {
            _propertyPresenter.EditComment();
            _contextFrame.IsVisible = _propertyPresenter.HasContext;
        }

        private async Task LoadWatermarkImageAsync()
        {
            if (TopLevel.GetTopLevel(this) is not { } owner) return;
            try
            {
                using var dialogScope = _dialogScopeProvider?.Invoke();
                var files = await owner.StorageProvider.OpenFilePickerAsync(new()
                    { AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
                if (files.Count == 0 || _disposed) return;
                await using var stream = await files[0].OpenReadAsync();
                using var memory = new MemoryStream();
                var buffer = new byte[16384];
                while (await stream.ReadAsync(buffer) is var count && count > 0)
                {
                    if (memory.Length + count > 16 * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("AnnotationWatermarkTooLarge"));
                    memory.Write(buffer, 0, count);
                }

                if (!_disposed) _session.SetWatermarkImage(memory.ToArray());
            }
            catch (Exception exception)
            {
                if (_disposed) return;
                _session.SetExportState(false, exception.Message);
            }
        }
    }
}
