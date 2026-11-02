using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private Window? _groupCreateDialog;
        private PinnedGroupsManagerWindow? _groupManager;

        internal IReadOnlyList<PinnedGroupDefinition> GroupDefinitions =>
            Preferences.Groups.Any(group => group.Id.Length == 0)
                ? Preferences.Groups
                : [new() { Id = "" }, .. Preferences.Groups];

        internal event Action? GroupsChanged;

        internal void ReorderGroups(IReadOnlyList<string> ids)
        {
            var groups = GroupDefinitions;
            if (ids.Count != groups.Count || ids.Distinct(StringComparer.Ordinal).Count() != groups.Count ||
                ids.Any(id => groups.All(group => group.Id != id)))
                throw new ArgumentException("Invalid pin group order.", nameof(ids));
            if (groups.Select(group => group.Id).SequenceEqual(ids, StringComparer.Ordinal)) return;
            var byId = groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
            CommitGroups(Preferences with { Groups = [.. ids.Select(id => byId[id] with { })] });
            PinLogger.ZLogInformation($"Pin groups reordered. GroupCount={ids.Count}");
        }

        internal (int Visible, int Total) GetGroupPinCounts(string id)
        {
            var states = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var state in _unrestoredStates.Values.Concat(
                         _unrestoredDocuments.Values.Select(entry => entry.Window)))
                Add(state.Id, state.Group, true);
            foreach (var state in _closedImages.Select(entry => entry.State)
                         .Concat(_closedDocuments.Select(entry => entry.State.Window)))
                Add(state.Id, state.Group, false);
            foreach (var window in AllWindows)
            {
                var pending = window switch
                {
                    PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var state) => state,
                    PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved) => saved
                        .Window,
                    _ => null,
                };
                if (pending is not null) Add(pending.Id, pending.Group, true);
                else Add(window.Id, window.Group, true);
            }

            return (states.Count(pair => pair.Value), states.Count);

            void Add(string pinId, string group, bool visible)
            {
                if (group == id) states[pinId] = visible;
            }
        }

        internal void CreateGroup(PinnedGroupDefinition value, bool activate)
        {
            if (value.Id.Length == 0 || !value.IsValid || GroupDefinitions.Any(group =>
                    group.Id == value.Id || group.Name == value.Name))
                throw new ArgumentException(UiText.Get("PinnedGroupDuplicate"));
            CommitGroups(Preferences with
            {
                Groups = [.. Preferences.Groups, value.Normalize()],
                ActiveGroup = activate ? value.Id : Preferences.ActiveGroup,
            });
        }

        internal void UpdateGroup(string id, PinnedGroupDefinition value)
        {
            if (GroupDefinitions.All(group => group.Id != id) || !value.IsValid || value.Name.Length == 0 ||
                GroupDefinitions.Any(group => group.Id != id && group.Name == value.Name))
                throw new ArgumentException(UiText.Get("PinnedGroupDuplicate"));
            CommitGroups(Preferences with
            {
                Groups =
                [
                    .. GroupDefinitions.Select(group => group.Id == id
                        ? (value with { Id = id }).Normalize()
                        : group),
                ],
            });
        }

        internal void ClearGroup(string id)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == id).ToArray()) window.DestroyForGroup();
            for (var index = _closedImages.Count - 1; index >= 0; index--)
                if (_closedImages[index].State.Group == id)
                    DropClosedImage(index);
            _closedDocuments.RemoveAll(entry => entry.State.Window.Group == id);
            foreach (var key in _unrestoredStates.Where(pair => pair.Value.Group == id).Select(pair => pair.Key)
                         .ToArray())
                _unrestoredStates.Remove(key);
            foreach (var key in _unrestoredDocuments.Where(pair => pair.Value.Window.Group == id)
                         .Select(pair => pair.Key).ToArray())
                _unrestoredDocuments.Remove(key);
            PinLogger.ZLogInformation($"Pin group cleared. GroupId={id}");
            ScheduleSave();
            GroupsChanged?.Invoke();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void RemoveGroup(string id, string? moveToGroup = null)
        {
            if (id.Length == 0 || GroupDefinitions.All(group => group.Id != id))
                throw new ArgumentException(UiText.Get("PinnedGroupDefault"));
            if (moveToGroup is not null)
            {
                if (moveToGroup == id || GroupDefinitions.All(group => group.Id != moveToGroup))
                    throw new ArgumentException(UiText.Get("PinnedGroupDuplicate"));
                foreach (var window in AllWindows.Where(window => window.Group == id)) window.SetGroup(moveToGroup);
                foreach (var state in _closedImages.Select(entry => entry.State)
                             .Concat(_closedDocuments.Select(entry => entry.State.Window))
                             .Concat(_unrestoredStates.Values)
                             .Concat(_unrestoredDocuments.Values.Select(entry => entry.Window)))
                    if (state.Group == id)
                        state.Group = moveToGroup;
                foreach (var state in _pendingRestore.Values
                             .Concat(_pendingDocuments.Values.Select(entry => entry.Window)))
                    if (state.Group == id)
                        state.Group = moveToGroup;
            }
            else
            {
                ClearGroup(id);
            }

            CommitGroups(Preferences with
            {
                Groups = [.. Preferences.Groups.Where(group => group.Id != id)],
                ActiveGroup = Preferences.ActiveGroup == id ? "" : Preferences.ActiveGroup,
            });
            if (moveToGroup == Preferences.ActiveGroup) RequestGroupRestore();
        }

        private void CommitGroups(PinnedImagePreferences preferences)
        {
            Preferences = preferences;
            ApplyGroupVisibility();
            PreferencesChanged?.Invoke(Preferences);
            GroupsChanged?.Invoke();
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void OpenGroupManager()
        {
            if (_groupManager is not null)
            {
                _groupManager.Activate();
                return;
            }

            var window = new PinnedGroupsManagerWindow(this);
            _groupManager = window;
            window.Closed += (_, _) => _groupManager = null;
            window.Show();
        }

        internal async Task CreateGroupInteractiveAsync(Window? owner = null)
        {
            if (_groupCreateDialog is not null)
            {
                _groupCreateDialog.Activate();
                return;
            }

            try
            {
                await PinnedGroupEditorDialog.ShowAsync(this, owner, window => _groupCreateDialog = window);
            }
            finally
            {
                _groupCreateDialog = null;
            }
        }

        private async Task CreateAndMoveToGroupAsync(IPinnedWindow source)
        {
            if (_groupCreateDialog is not null)
            {
                _groupCreateDialog.Activate();
                return;
            }

            try
            {
                var id = await PinnedGroupEditorDialog.ShowAsync(this, null, window => _groupCreateDialog = window);
                if (id is not null && AllWindows.Contains(source)) SetSelectionGroup(source, id);
            }
            finally
            {
                _groupCreateDialog = null;
            }
        }

        private string ResolveGroupName(string name)
        {
            if (name.Length == 0) return Preferences.ActiveGroup;
            var existing = GroupDefinitions.FirstOrDefault(group => group.Name == name);
            if (existing is not null) return existing.Id;
            var created = new PinnedGroupDefinition { Name = name };
            CreateGroup(created, false);
            return created.Id;
        }
    }
}
