using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly List<(PinnedDocumentState State, long ClosedAt)> _closedDocuments = [];
        private readonly List<PinnedDocumentWindow> _documentWindows = [];
        private IEnumerable<IPinnedWindow> AllWindows => _windows.Cast<IPinnedWindow>().Concat(_documentWindows);

        internal PinnedDocumentWindow? PinDocument(PinnedDocument document)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Preferences.DuplicateBehavior == PinnedDuplicateBehavior.RestoreLastClosed)
            {
                var index = _closedDocuments.FindLastIndex(item =>
                    item.State.Window.Group == Preferences.ActiveGroup && SameDocument(item.State.Document, document));
                if (index >= 0)
                {
                    RestoreClosedDocument(index);
                    return null;
                }
            }

            if (Preferences.DuplicateBehavior != PinnedDuplicateBehavior.CreateNew &&
                _documentWindows.FirstOrDefault(item =>
                    item.Group == Preferences.ActiveGroup && SameDocument(item.Document, document)) is { } existing)
            {
                if (Preferences.DuplicateBehavior is PinnedDuplicateBehavior.FocusExisting or
                    PinnedDuplicateBehavior.RestoreLastClosed)
                {
                    RevealWindow(existing);
                    if (Preferences.DuplicateBehavior == PinnedDuplicateBehavior.FocusExisting) existing.Activate();
                }

                return null;
            }

            var window = CreateDocumentWindow(document);
            try
            {
                ShowNewPin(window);
            }
            catch
            {
                _documentWindows.Remove(window);
                window.Dispose();
                throw;
            }

            PinLogger.ZLogInformation($"Pinned document opened. Kind={document.GetType().Name}");
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            return window;
        }


        public void PinFormulas(IReadOnlyList<string> sources)
        {
            ArgumentNullException.ThrowIfNull(sources);
            if (sources.Count == 0) throw new ArgumentException("At least one formula is required.", nameof(sources));
            PinDocument(new PinnedFormulaDocument([.. sources]));
        }

        private static bool SameDocument(PinnedDocument left, PinnedDocument right)
        {
            if (left is PinnedFormulaDocument leftFormula && right is PinnedFormulaDocument rightFormula)
                return leftFormula.FontSize.Equals(rightFormula.FontSize) &&
                       leftFormula.TextColor == rightFormula.TextColor &&
                       leftFormula.Sources.SequenceEqual(rightFormula.Sources, StringComparer.Ordinal);
            return left is PinnedFilesDocument leftFiles && right is PinnedFilesDocument rightFiles
                ? leftFiles.Paths.SequenceEqual(rightFiles.Paths,
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                : left == right;
        }

        private PinnedDocumentWindow CreateDocumentWindow(PinnedDocument document)
        {
            if (document is PinnedHtmlDocument html) _ = PinnedRichParser.ReadHtml(html.Html);
            var window = new PinnedDocumentWindow(document, () => CanRestoreHidden(), Preferences)
            {
                PreferencesProvider = () => Preferences,
                SavePreferencesProvider = () => SavePreferencesProvider(),
                ClipboardPreferencesProvider = () => ClipboardPreferencesProvider(),
                OutputDirectoryProvider = () => OutputDirectoryProvider(),
                GroupsProvider = () => Groups,
                GroupChangeRequested = SetSelectionGroup,
                NewGroupRequested = CreateAndMoveToGroupAsync,
                SelectionRequested = ToggleSelection,
                CloseRequested = CloseSelection,
                MoveSelectionRequested = MoveSelection,
                CanArrangeSelectionProvider = CanArrangeSelection,
                ArrangeSelectionRequested = ArrangeSelection,
                SaveSelectionRequested = SaveSelectionAsync,
                SavedPathChanged = path => SavedPathChanged?.Invoke(path),
                GroupVisibilityRequested = (group, visible) =>
                {
                    if (visible) ShowGroup(group);
                    else HideGroup(group);
                },
            };
            _documentWindows.Add(window);
            AttachPresentation(window);
            window.StateChanged += WindowStateChanged;
            window.Closed += (_, _) =>
            {
                if (!_documentWindows.Remove(window)) return;
                _selectedImages.Remove(window);
                ForgetPresentation(window);
                _pendingDocuments.Remove(window);
                window.StateChanged -= WindowStateChanged;
                if (!_disposed)
                {
                    if (!window.WasDestroyed)
                    {
                        _closedDocuments.Add((window.CaptureDocumentState(), DateTimeOffset.UtcNow.UtcTicks));
                        TrimClosedHistory();
                    }

                    ScheduleSave();
                }

                Changed?.Invoke(this, EventArgs.Empty);
            };
            return window;
        }

        private bool RestoreClosedDocument(int index)
        {
            var saved = _closedDocuments[index].State;
            var window = CreateDocumentWindow(saved.Document);
            try
            {
                window.Show();
                saved.Window.Visible = true;
                window.RestoreDocumentState(saved);
                window.RevealImage();
            }
            catch
            {
                _documentWindows.Remove(window);
                window.Dispose();
                throw;
            }

            _closedDocuments.RemoveAt(index);
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void TrimMixedHistory()
        {
            while (_closedDocuments.Count + _closedImages.Count > Preferences.HistoryLimit ||
                   _closedDocuments.Sum(item => DocumentBytes(item.State.Document)) +
                   _closedImages.Sum(item => item.EstimatedBytes) > 128L * 1024 * 1024)
                if (_closedDocuments.Count > 0 && (_closedImages.Count == 0 ||
                                                   _closedDocuments[0].ClosedAt < _closedImages[0].ClosedAt))
                    _closedDocuments.RemoveAt(0);
                else DropClosedImage(0);
        }

        private static long DocumentBytes(PinnedDocument document)
        {
            return document switch
            {
                PinnedFormulaDocument formula => formula.Sources.Sum(source => (long)source.Length * sizeof(char)),
                PinnedHtmlDocument html => ((long)html.Html.Length + html.Text.Length) * sizeof(char),
                PinnedColorDocument color => ((long)color.Text.Length + color.Html.Length + color.Value.Length) *
                                             sizeof(char),
                PinnedFilesDocument files => files.Paths.Sum(path => (long)path.Length * sizeof(char)),
                _ => (long)document.PlainText.Length * sizeof(char),
            };
        }

        private void SetSelectionGroup(IPinnedWindow source, string group)
        {
            var selection = SelectionSnapshot(source);
            if (selection.Length == 0) source.SetGroup(group);
            else
                foreach (var window in selection)
                    window.SetGroup(group);
            foreach (var selected in _selectedImages) selected.SetSelected(false);
            _selectedImages.Clear();
            ApplyGroupVisibility();
            ScheduleSave();
            GroupsChanged?.Invoke();
        }
    }
}
