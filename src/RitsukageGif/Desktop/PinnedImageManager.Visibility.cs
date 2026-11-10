using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly PinnedVisibilityMask<IPinnedWindow> _visibilityMask = new();
        private PinnedGroupsWindow? _groupSwitcher;
        private long _nextRegistrationOrder;
        private bool AllPinsHidden => _visibilityMask.Active;
        public Action<PinnedImagePreferences>? PreferencesChanged { get; set; }

        private void AttachPresentation(IPinnedWindow window)
        {
            window.Presentation.RegistrationOrder = ++_nextRegistrationOrder;
            window.Window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.IsVisibleProperty) Changed?.Invoke(this, EventArgs.Empty);
            };
            window.Presentation.HideOthersRequested = () => ToggleOtherPins(window);
            window.Presentation.HideOthersCheckedProvider = () => AllPinsHidden;
            window.Presentation.GroupCountsProvider = GetGroupPinCounts;
            window.Presentation.SelectionMenuProvider = () => CreateSelectionMenu(window);
            window.Presentation.ConfigurationVisibleProvider = () =>
                OpenConfigurationRequested is not null && !TrayVisibleProvider();
            window.Presentation.ConfigurationRequested =
                () => OpenConfigurationRequested?.Invoke() ?? Task.CompletedTask;
            window.Presentation.SwitchNextRequested = () => SwitchToNextPin(window);
            AttachDrop(window);
        }

        internal bool ToggleOtherPins(IPinnedWindow? source = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (AllPinsHidden)
            {
                RestoreMaskedPins();
                return true;
            }

            var protectedPins = source is not null && !_selectedImages.Contains(source)
                ? [source]
                : _selectedImages.Where(window => window.IsVisible).ToArray();
            if (protectedPins.Length == 0) return false;
            MaskVisiblePins(protectedPins);
            return true;
        }

        internal void ToggleAllPinnedVisibility()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (AllPinsHidden) RestoreMaskedPins();
            else MaskVisiblePins([]);
        }

        private void MaskVisiblePins(IEnumerable<IPinnedWindow> protectedPins)
        {
            var visible = AllWindows.Where(window => window.IsVisible).ToArray();
            var windows = visible.Select(window => window.Window).ToArray();
            Window.SortWindowsByZOrder(windows);
            var lookup = visible.ToDictionary(window => window.Window);
            _visibilityMask.Begin(windows.Select(window => lookup[window]),
                protectedPins.ToHashSet<IPinnedWindow>(ReferenceEqualityComparer.Instance));
            foreach (var window in _visibilityMask.Order)
                if (_visibilityMask.IsHidden(window))
                    window.HideImage();
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RestoreMaskedPins()
        {
            if (!AllPinsHidden) return;
            var order = _visibilityMask.Order.ToArray();
            var hidden = order.Where(window => _visibilityMask.IsHidden(window)).ToArray();
            _visibilityMask.Clear();
            foreach (var window in hidden)
                if (window.Group == Preferences.ActiveGroup) RevealWindow(window);
                else RestoreMaskedGroupIntent(window);
            PinnedWindowOrder.Restore(order);
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RestoreMaskedGroupIntent(IPinnedWindow window)
        {
            window.Presentation.SetGroupIntent(true);
            switch (window)
            {
                case PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var state):
                    state.VisibleInGroup = true;
                    break;
                case PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved):
                    saved.Window.VisibleInGroup = true;
                    break;
            }
        }

        private void PrepareNewPinInput()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (AllPinsHidden && !Preferences.KeepOthersHiddenOnNewPin) RestoreMaskedPins();
        }

        private void MaskRestoredWindow(IPinnedWindow window, PinnedWindowState state)
        {
            if (!AllPinsHidden || !(state.GroupHidden ? state.VisibleInGroup : state.Visible)) return;
            _visibilityMask.Attach(window, false);
            state.Visible = false;
            window.Presentation.SetUserVisibility(false);
        }

        private void CaptureMaskVisibility(IPinnedWindow window, PinnedWindowState state)
        {
            if (!_visibilityMask.IsHidden(window)) return;
            if (state.GroupHidden) state.VisibleInGroup = true;
            else state.Visible = true;
        }

        private void ForgetPresentation(IPinnedWindow window)
        {
            _visibilityMask.Remove(window);
        }

        private void ShowNewPin(IPinnedWindow window)
        {
            _visibilityMask.Attach(window, true);
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
            RestoreMaskedPins();
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
                    case PinnedImageWindow image:
                        _pendingRestore.Remove(image);
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
