using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private void ConfigureFileMenu(ContextMenu menu)
        {
            if (Document is not PinnedFilesDocument files) return;
            Point? requestedPoint = null;
            Button? highlighted = null;
            IBrush? previousBackground = null;
            _content.AddHandler(ContextRequestedEvent, (_, args) =>
            {
                if (!args.Handled && args.TryGetPosition(_content, out var point)) requestedPoint = point;
            }, RoutingStrategies.Tunnel);

            var open = FileCommand("PinnedFileOpen", path =>
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                return Task.CompletedTask;
            });
            var copyThis = FileCommand("PinnedFileCopy", path => CopyFilesAsync([path]));
            var copyAll = Command(files.Paths.Length == 1 ? "PinnedFileCopySingle" : "PinnedFileCopyAll",
                () => CopyFilesAsync(files.Paths));
            var pathThis = FileCommand("PinnedFileCopyPath", path => CopyTextAsync(FileClipboardPath(path)));
            var pathAll = Command(files.Paths.Length == 1 ? "PinnedFileCopySinglePath" : "PinnedFileCopyAllPaths",
                () => CopyTextAsync(string.Join('\n', files.Paths.Select(FileClipboardPath))));
            var reveal = FileCommand("PinnedFileReveal", PlatformShell.RevealFileAsync);
            foreach (var item in new Control[] { open, copyThis, copyAll, pathThis, pathAll, reveal, new Separator() })
                menu.Items.Add(item);

            menu.Opened += (_, _) =>
            {
                var point = requestedPoint;
                requestedPoint = null;
                if (point is null && PinnedGroupPointer.Read() is { } pointer)
                    point = _content.PointToClient(pointer);
                var visual = point is { } position ? _content.InputHitTest(position) as Visual : null;
                var button = visual as Button ?? visual?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
                var hit = button?.Tag is string path && files.Paths.Contains(path, StringComparer.Ordinal) &&
                          button.GetVisualAncestors().Contains(_fileList!)
                    ? path
                    : null;
                open.IsVisible = hit is not null;
                copyThis.IsVisible = pathThis.IsVisible = hit is not null && files.Paths.Length != 1;
                open.CommandParameter = copyThis.CommandParameter = pathThis.CommandParameter = hit;
                reveal.CommandParameter = hit ?? files.Paths[0];
                if (hit is null || button is null) return;
                highlighted = button;
                previousBackground = button.Background;
                var accent = AppTheme.Accent;
                button.Background =
                    new SolidColorBrush(Color.FromArgb(40, accent.Color.R, accent.Color.G, accent.Color.B));
            };
            menu.Closed += (_, _) =>
            {
                requestedPoint = null;
                highlighted?.Background = previousBackground;
                highlighted = null;
                previousBackground = null;
            };
        }

        private MenuItem FileCommand(string key, Func<string, Task> execute)
        {
            var item = new MenuItem();
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
            item.Click += (_, _) => _ = Presentation.RunMenuAsync(() => RunAsync(() =>
                item.CommandParameter is string path ? execute(path) : Task.CompletedTask));
            return item;
        }

        private static string FileClipboardPath(string path)
        {
            return OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;
        }
    }
}
