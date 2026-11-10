using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private Point[] _fileSceneRows = [];

        private async Task<RenderTargetBitmap> RenderFileSceneAsync(double scale, CancellationToken token)
        {
            using var scene = CreateFileScene(token);
            foreach (var row in scene.List.GetVisualDescendants().OfType<PinnedFileRowButton>())
                await row.EnsureIconAsync(token);
            token.ThrowIfCancellationRequested();
            var output = CreateDocumentSceneBitmap(scene.List.Bounds.Size, scale);
            try
            {
                output.Render(scene.List);
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private T WithFileScene<T>(Func<ItemsControl, T> read, CancellationToken token)
        {
            using var scene = CreateFileScene(token);
            return read(scene.List);
        }

        private PinnedFileSceneLease CreateFileScene(CancellationToken token)
        {
            var list = CreateFileGrid(false);
            var host = new Canvas { Opacity = 0, IsHitTestVisible = false };
            host.Children.Add(list);
            _content.Children.Add(host);
            var scene = new PinnedFileSceneLease(_content, host, list);
            try
            {
                list.ApplyStyling();
                list.ApplyTemplate();
                var size = FileLayoutSnapshot.Extent;
                list.Measure(size);
                list.Arrange(new(default, size));
                list.UpdateLayout();
                var rows = new Point[FileLayoutSnapshot.Entries.Count];
                for (var index = 0; index < rows.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var container = list.ContainerFromIndex(index)
                                    ?? throw new InvalidOperationException("The file entry was not laid out.");
                    rows[index] = container.TranslatePoint(default, list)!.Value;
                }

                _fileSceneRows = rows;
                return scene;
            }
            catch
            {
                scene.Dispose();
                throw;
            }
        }

        private PinnedFileRowButton CreateFileItem(PinnedFileDisplayEntry entry, bool automatic, double imageScale)
        {
            var file = new PinnedFileRowButton(entry, automatic, imageScale, _lifetime.Token);
            file.DoubleTapped += async (_, args) =>
            {
                args.Handled = true;
                await RunAsync(() =>
                {
                    Process.Start(new ProcessStartInfo(entry.Path!) { UseShellExecute = true })?.Dispose();
                    return Task.CompletedTask;
                });
            };
            return file;
        }
    }
}
