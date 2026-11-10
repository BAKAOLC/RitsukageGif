using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private PinnedFileListMaximumSize? _fileListMaximumSize;
        private PinnedFilesLayoutSnapshot? _fileLayoutSnapshot;

        private PinnedFilesLayoutSnapshot FileLayoutSnapshot => _fileLayoutSnapshot ??=
            PinnedFilesLayoutSnapshot.Create(((PinnedFilesDocument)Document).Paths,
                _fileListMaximumSize ??= CreateFileListMaximumSize());

        private PinnedFileListMaximumSize CreateFileListMaximumSize()
        {
            var screen = Screens.Primary ?? throw new InvalidOperationException("The primary screen is unavailable.");
            var scale = OperatingSystem.IsMacOS() ? 1 : screen.Scaling;
            return new(PinnedFilesLayoutSnapshot.Round(screen.Bounds.Width / 3d) / scale,
                PinnedFilesLayoutSnapshot.Round(screen.Bounds.Height / 3d) / scale);
        }

        private ItemsControl CreateFileGrid(bool automatic, double imageScale = 1)
        {
            var layout = FileLayoutSnapshot;
            return new()
            {
                ItemsSource = layout.Entries, Width = layout.Extent.Width, Height = layout.Extent.Height,
                BorderThickness = new(0), Background = Brushes.White,
                ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid
                {
                    Columns = layout.Columns, Rows = layout.Rows,
                }),
                ItemTemplate = new FuncDataTemplate<PinnedFileDisplayEntry>((entry, _) =>
                    CreateFileDisplayItem(entry, automatic, automatic ? _zoom : imageScale)),
            };
        }

        private void RefreshFileIconSampling()
        {
            if (_fileList is null) return;
            foreach (var row in _fileList.GetVisualDescendants().OfType<PinnedFileRowButton>())
                row.RefreshIconSampling(_zoom);
        }

        private Border CreateFileDisplayItem(PinnedFileDisplayEntry entry, bool automatic, double imageScale)
        {
            var cell = FileLayoutSnapshot.CellSize;
            return new()
            {
                Width = cell.Width, Height = cell.Height,
                Child = entry.Path is not null
                    ? CreateFileItem(entry, automatic, imageScale)
                    : new TextBlock
                    {
                        Text = UiText.Format("PinnedFilesTotal", entry.TotalCount), FontSize = 20,
                        FontWeight = FontWeight.Bold, Foreground = AppTheme.Accent,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
            };
        }
    }
}
