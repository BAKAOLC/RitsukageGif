using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private long _createdAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        private double? _opacityBeforeThrough;
        private double? _throughAppliedOpacity;
        public string Id { get; private set; } = Guid.NewGuid().ToString("N");
        public string Group { get; private set; } = "";
        private bool IsLocked { get; set; }
        public bool IsInteractionLocked => IsOperationLocked(PinnedLockedOperations.Move);
        public bool IsCloseLocked => IsOperationLocked(PinnedLockedOperations.Close);
        private bool IsMouseThrough { get; set; }
        public ReadOnlyMemory<byte> OriginalPng => _png;
        public Func<IReadOnlyList<string>> GroupsProvider { get; set; } = () => [];
        public Action<string, bool>? GroupVisibilityRequested { get; set; }
        public event EventHandler? StateChanged;

        private void AddInteractionMenu(ContextMenu menu)
        {
            PositionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
            var locked = new MenuItem
            {
                Tag = "lock",
                ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageLock"),
            };
            locked.Click += (_, _) => SetLocked(!IsLocked);
            var through = new MenuItem
            {
                Tag = "through",
                ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageMouseThrough"),
            };
            through.Click += async (_, _) => await RunAsync(() =>
            {
                SetMouseThrough(!IsMouseThrough);
                return Task.CompletedTask;
            });
            var group = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageGroup") };
            var topmost = new MenuItem
            {
                Tag = "topmost",
                ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageTopmost"),
            };
            topmost.Click += (_, _) => SetTopmost(!Topmost);
            menu.Items.Add(locked);
            menu.Items.Add(through);
            menu.Items.Add(topmost);
            menu.Items.Add(group);
            menu.Opened += (_, _) =>
            {
                locked.IsChecked = IsLocked;
                locked.IsEnabled = _annotation is null;
                through.IsChecked = IsMouseThrough;
                topmost.IsChecked = Topmost;
                through.IsEnabled = _annotation is null && OperatingSystem.IsWindows() &&
                                    (_canRestoreHidden() || IsMouseThrough);
                group.Items.Clear();
                AddGroupChoice(group, "", UiText.Get("PinnedImageUngrouped"));
                foreach (var name in GroupsProvider()) AddGroupChoice(group, name, name);
                group.Items.Add(new Separator());
                var create = new MenuItem
                    { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageNewGroup") };
                create.Click += async (_, _) => await EditGroupAsync();
                group.Items.Add(create);
                var hide = new MenuItem
                    { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageHideGroup") };
                hide.Click += (_, _) => GroupVisibilityRequested?.Invoke(Group, false);
                group.Items.Add(hide);
                var show = new MenuItem
                    { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageShowGroup") };
                show.Click += (_, _) => GroupVisibilityRequested?.Invoke(Group, true);
                group.Items.Add(show);
            };
        }

        private void AddGroupChoice(MenuItem parent, string name, string label)
        {
            var item = new MenuItem
                { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = Group == name };
            item.Click += (_, _) => SetGroup(name);
            parent.Items.Add(item);
        }

        private async Task EditGroupAsync()
        {
            var box = new TextBox { Text = Group, MaxLength = 80 };
            var save = new Button { [!ContentProperty] = LiveText.Resource("EditorSave") };
            var cancel = new Button { [!ContentProperty] = LiveText.Resource("Close") };
            var dialog = new Window
            {
                [!TitleProperty] = LiveText.Resource("PinnedImageNewGroup"), Width = 360,
                SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = AppTheme.Surface,
                Content = new StackPanel
                {
                    Margin = new(16), Spacing = 12, Children =
                    {
                        box,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8, Children = { save, cancel },
                        },
                    },
                },
            };
            save.Click += (_, _) =>
            {
                SetGroup(box.Text ?? "");
                dialog.Close();
            };
            cancel.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }

        public void SetGroup(string value)
        {
            value = value.Trim();
            if (value.Length > 80) throw new ArgumentOutOfRangeException(nameof(value));
            if (Group == value) return;
            Group = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetTopmost(bool value)
        {
            if (Topmost == value) return;
            Topmost = value;
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetLocked(bool value)
        {
            if (IsLocked == value) return;
            if (value) EndAnnotation();
            IsLocked = value;
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetMouseThrough(bool value)
        {
            if (IsMouseThrough == value) return;
            switch (value)
            {
                case true when !_canRestoreHidden():
                    throw new InvalidOperationException(UiText.Get("PinnedImageRestoreRequired"));
                case true:
                    EndAnnotation();
                    var previous = Opacity;
                    if (Opacity > CurrentPreferences.MouseThroughOpacity)
                    {
                        _opacityBeforeThrough = Opacity;
                        _throughAppliedOpacity = CurrentPreferences.MouseThroughOpacity;
                        Opacity = CurrentPreferences.MouseThroughOpacity;
                    }

                    try
                    {
                        ApplyMouseThrough(true);
                    }
                    catch
                    {
                        Opacity = previous;
                        _opacityBeforeThrough = null;
                        _throughAppliedOpacity = null;
                        throw;
                    }

                    break;
                case false:
                    ApplyMouseThrough(false);
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (_opacityBeforeThrough is { } restored && Opacity == _throughAppliedOpacity)
                        Opacity = restored;
                    _opacityBeforeThrough = null;
                    _throughAppliedOpacity = null;
                    break;
            }

            IsMouseThrough = value;
            RefreshWindowShadow();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RestoreInteraction()
        {
            SetMouseThrough(false);
            SetLocked(false);
            RevealImage();
        }

        public void RevealImage()
        {
            var wasHidden = !IsVisible || WindowState == WindowState.Minimized;
            Show();
            WindowState = WindowState.Normal;
            RestoreToScreen();
            if (wasHidden) PinLogger.ZLogInformation($"Pinned image visibility restored.");
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (!_closed && (change.Property == OpacityProperty ||
                             change.Property == IsVisibleProperty || change.Property == TopmostProperty))
                StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public PinnedImageState CaptureState()
        {
            return new()
            {
                Id = Id, Group = Group, X = Position.X, Y = Position.Y, Zoom = _zoom,
                CaptureScaling = OriginalCaptureScaling,
                Opacity = Opacity, Locked = IsLocked, Visible = IsVisible && WindowState != WindowState.Minimized,
                ScrollX = _viewport.Offset.X, ScrollY = _viewport.Offset.Y, Topmost = Topmost,
                CreatedAtUtcTicks = _createdAtUtcTicks, Thumbnail = _isThumbnail,
                ThumbnailX = _thumbnailRegion?.X ?? 0, ThumbnailY = _thumbnailRegion?.Y ?? 0,
                ThumbnailWidth = _thumbnailRegion?.Width ?? 0, ThumbnailHeight = _thumbnailRegion?.Height ?? 0,
            };
        }

        public void RestoreState(PinnedImageState state)
        {
            SetOriginalCaptureScaling(state.CaptureScaling);
            Id = state.Id;
            Group = state.Group;
            _zoom = state.Zoom;
            Opacity = state.Opacity;
            IsLocked = state.Locked;
            Topmost = state.Topmost;
            if (state.CreatedAtUtcTicks > 0) _createdAtUtcTicks = state.CreatedAtUtcTicks;
            if (state is { ThumbnailWidth: > 0, ThumbnailHeight: > 0, ThumbnailX: >= 0, ThumbnailY: >= 0 } &&
                (long)state.ThumbnailX + state.ThumbnailWidth <= _bitmap.Size.Width &&
                (long)state.ThumbnailY + state.ThumbnailHeight <= _bitmap.Size.Height)
            {
                _thumbnailRegion = new(state.ThumbnailX, state.ThumbnailY, state.ThumbnailWidth, state.ThumbnailHeight);
                _isThumbnail = state.Thumbnail;
            }

            UpdateSize();
            RefreshPreferencePresentation();
            Position = new(state.X, state.Y);
            _viewport.Offset = new(state.ScrollX, state.ScrollY);
            RestoreToScreen();
            if (!state.Visible) HideImage();
        }
    }
}
