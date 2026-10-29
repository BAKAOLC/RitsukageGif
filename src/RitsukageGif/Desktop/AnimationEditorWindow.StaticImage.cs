using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private Task LoadScreenshotAsync(byte[] png)
        {
            return RunAsync(async token =>
            {
                var document = await Task.Run(() => AnimationEditDocument.FromImage(png, token), token);
                await InstallImageDocumentAsync(document, null, true, token);
            });
        }

        private async Task InstallImageDocumentAsync(AnimationEditDocument document, string? path, bool dirty,
            CancellationToken token)
        {
            try
            {
                await _canvas.LoadFrameAsync(document.Frames[0].Path, token);
            }
            catch
            {
                document.Dispose();
                throw;
            }

            _displayedPath = document.Frames[0].Path;
            _thumbnailCache.Clear();
            _document?.Dispose();
            _document = document;
            _path = path;
            _dirty = dirty;
            Refresh();
            _canvas.Fit();
        }

        private static FilePickerFileType[] StaticImageFiles()
        {
            return
            [
                new("PNG") { Patterns = ["*.png"] },
                new("JPEG") { Patterns = ["*.jpg", "*.jpeg"] },
                new("BMP") { Patterns = ["*.bmp"] },
            ];
        }

        private async Task SaveImageAsync(bool saveAs)
        {
            if (_document is null) return;
            var path = _path;
            if (saveAs || path is null || !IsStaticExtension(path))
            {
                var extension = _format.SelectedIndex switch { 1 => "jpg", 2 => "bmp", _ => "png" };
                var formats = StaticImageFiles();
                var formatIndex = Math.Clamp(_format.SelectedIndex, 0, formats.Length - 1);
                (formats[0], formats[formatIndex]) = (formats[formatIndex], formats[0]);
                var file = await StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = UiText.Get("ImageSaveAs"), SuggestedFileName =
                        $"{(path is null ? "image" : Path.GetFileNameWithoutExtension(path))}.{extension}",
                    DefaultExtension = extension, ShowOverwritePrompt = true, FileTypeChoices = formats,
                });
                if (file?.TryGetLocalPath() is not { } selected) return;
                path = selected;
            }

            await SaveImageToPathAsync(path);
        }

        private Task SaveImageToPathAsync(string path)
        {
            return RunAsync(async token =>
            {
                if (_document is null) return;
                var bytes = _canvas.ExportPng();
                await new StaticImageService().SaveAsync(bytes, path,
                    new() { Quality = Value(_exportQuality), Overwrite = true }, token);
                if (_canvas.HasChanges)
                {
                    await Task.Run(() => _document.ReplaceFrames([0], bytes, token), token);
                    await _canvas.LoadFrameAsync(_document.Frames[0].Path, token);
                    _displayedPath = _document.Frames[0].Path;
                }

                _path = path;
                _dirty = false;
                Refresh();
                SetStatus("AnimationExported", path);
            });
        }

        private static bool IsStaticExtension(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() is
                ".png" or ".jpg" or ".jpeg" or ".bmp";
        }

        private async Task CommitImageDraftAsync(CancellationToken token)
        {
            if (_document is null || !_canvas.HasChanges) return;
            var bytes = _canvas.ExportPng();
            await Task.Run(() => _document.ReplaceFrames([0], bytes, token), token);
            _dirty = true;
            await _canvas.LoadFrameAsync(_document.Frames[0].Path, token);
            _displayedPath = _document.Frames[0].Path;
        }

        private Task CopyImageAsync()
        {
            return RunAsync(async token =>
            {
                if (_document is null || Clipboard is not { } clipboard) return;
                token.ThrowIfCancellationRequested();
                await ClipboardImage.CopyAsync(clipboard, _canvas.ExportPng());
                SetStatus("ImageCopied");
            });
        }
    }
}
