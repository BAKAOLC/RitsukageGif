using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly HashSet<string> _isolatedVisibleIds = new(StringComparer.Ordinal);
        private readonly Dictionary<IPinnedWindow, string> _presentationIds = [];
        private PinnedGroupsWindow? _groupSwitcher;
        private bool _isolating;
        public Action<PinnedImagePreferences>? PreferencesChanged { get; set; }

        private void AttachPresentation(IPinnedWindow window)
        {
            _presentationIds.Add(window, window.Id);
            window.Window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.IsVisibleProperty) Changed?.Invoke(this, EventArgs.Empty);
            };
            window.Presentation.HideOthersRequested = () => ToggleOtherPins(window);
            window.Presentation.SwitchNextRequested = () => SwitchToNextPin(window);
            AttachDrop(window);
        }

        internal bool ToggleOtherPins(IPinnedWindow? source = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_isolating)
            {
                RestoreIsolatedPins();
                return true;
            }

            var protectedPins = source is not null && !_selectedImages.Contains(source)
                ? [source]
                : _selectedImages.Where(window => window.Group == Preferences.ActiveGroup && window.IsVisible)
                    .ToArray();
            if (protectedPins.Length == 0) return false;
            var protectedIds = protectedPins.Select(window => window.Id).ToHashSet(StringComparer.Ordinal);
            _isolatedVisibleIds.Clear();
            foreach (var window in AllWindows.Where(window =>
                         window.Group == Preferences.ActiveGroup && window.IsVisible))
            {
                _isolatedVisibleIds.Add(window.Id);
                if (!protectedIds.Contains(window.Id)) window.HideImage();
            }

            _isolating = true;
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void RestoreIsolatedPins()
        {
            if (!_isolating) return;
            _isolating = false;
            foreach (var window in AllWindows)
                if (_isolatedVisibleIds.Contains(window.Id) && window.Group == Preferences.ActiveGroup)
                    RevealWindow(window);
            _isolatedVisibleIds.Clear();
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ForgetPresentation(IPinnedWindow window)
        {
            if (_presentationIds.Remove(window, out var previousId)) _isolatedVisibleIds.Remove(previousId);
            _isolatedVisibleIds.Remove(window.Id);
        }

        private void RefreshPresentationIdentity(IPinnedWindow window)
        {
            if (!_presentationIds.TryGetValue(window, out var previousId) || previousId == window.Id) return;
            if (_isolatedVisibleIds.Remove(previousId)) _isolatedVisibleIds.Add(window.Id);
            _presentationIds[window] = window.Id;
        }

        private void ShowNewPin(IPinnedWindow window)
        {
            if (_isolating && !Preferences.KeepOthersHiddenOnNewPin) RestoreIsolatedPins();
            if (window.Group.Length == 0) window.SetGroup(Preferences.ActiveGroup);
            window.Presentation.SetUserVisibility(true);
            if (window.Group != Preferences.ActiveGroup)
            {
                window.Presentation.HideForGroup(true);
            }
            else
            {
                var activate = window.Window.ShowActivated;
                window.Window.ShowActivated = Preferences.ActivateOnNewPin;
                window.Window.Show();
                window.Window.ShowActivated = activate;
                if (Preferences.FlashOnNewPin) window.Presentation.Attention.Flash();
            }
        }

        private void SwitchToNextPin(IPinnedWindow source)
        {
            var visible = AllWindows.Where(window => window.Group == source.Group && window.IsVisible).ToArray();
            if (visible.Length < 2) return;
            var current = Array.IndexOf(visible, source);
            visible[(current + 1) % visible.Length].Window.Activate();
        }

        internal void SwitchPinGroup()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_groupSwitcher is not null)
            {
                _groupSwitcher.Advance();
                return;
            }

            var groups = GroupDefinitions;
            var picker = new PinnedGroupsWindow(groups, Preferences.ActiveGroup,
                PinnedKeyboardModifiers.Read() != KeyModifiers.None, SetActiveGroup,
                PinnedGroupPointer.Read() ??
                AllWindows.FirstOrDefault(window => window.Window.IsActive)?.Window.Position);
            _groupSwitcher = picker;
            picker.Closed += (_, _) => _groupSwitcher = null;
            picker.Show();
            picker.Advance();
        }

        private void SetActiveGroup(string group)
        {
            if (group == Preferences.ActiveGroup || GroupDefinitions.All(value => value.Id != group)) return;
            RestoreIsolatedPins();
            foreach (var selected in _selectedImages) selected.SetSelected(false);
            _selectedImages.Clear();
            Preferences = Preferences with { ActiveGroup = group };
            _requestedGroupAttention = group;
            PinLogger.ZLogInformation($"Active pin group changed. GroupId={group}");
            PreferencesChanged?.Invoke(Preferences);
            GroupsChanged?.Invoke();
            ScheduleSave();
            if (!RequestGroupRestore()) FlashRequestedGroupAttention();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyGroupVisibility()
        {
            foreach (var window in AllWindows)
            {
                switch (window)
                {
                    case PinnedImageWindow image when _pendingRestore.Remove(image, out var state):
                        image.RestoreState(state);
                        break;
                    case PinnedDocumentWindow document when _pendingDocuments.Remove(document, out var saved):
                        document.RestoreDocumentState(saved);
                        break;
                }

                if (window.Group != Preferences.ActiveGroup) window.Presentation.HideForGroup();
                else window.Presentation.RevealGroup();
            }
        }
    }
}
