using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private long _createdAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        private double? _opacityBeforeThrough;
        private bool IsLocked { get; set; }
        private bool IsMouseThrough { get; set; }
        public Func<IReadOnlyList<PinnedGroupDefinition>> GroupsProvider { get; set; } = () => [];
        public Action<IPinnedWindow, string>? GroupChangeRequested { get; set; }
        public Func<IPinnedWindow, Task>? NewGroupRequested { get; set; }
        public string Id { get; private set; } = Guid.NewGuid().ToString("N");
        public string Group { get; private set; } = "";
        public bool IsInteractionLocked => IsOperationLocked(PinnedLockRestrictions.Move);
        public bool IsIncomingDropLocked => IsOperationLocked(PinnedLockRestrictions.IncomingDrop);
        public bool IsCloseLocked => PinnedInputContext.IsCloseBlocked(IsLocked, CurrentPreferences.LockRestrictions);

        public void SetGroup(string value)
        {
            if (value.Length != 0 && !Guid.TryParseExact(value, "N", out _))
                throw new ArgumentException("Invalid pin group identifier.", nameof(value));
            if (Group == value) return;
            Group = value;
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
            Presentation.SetUserVisibility(true);
            var wasHidden = !IsVisible || WindowState == WindowState.Minimized;
            Show();
            WindowState = WindowState.Normal;
            RestoreToScreen();
            if (wasHidden) PinLogger.ZLogInformation($"Pinned image visibility restored.");
        }

        public event EventHandler? StateChanged;

        private void AddInteractionMenu(ContextMenu menu)
        {
            PositionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
            var locked = new MenuItem
            {
                Tag = "lock",
                ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedMenuLock"),
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
            {
                Tag = "move-group",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedMoveToGroup"),
            };
            var topmost = new MenuItem
            {
                Tag = "topmost",
                ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedMenuTopmost"),
            };
            topmost.Click += (_, _) => SetTopmost(!Topmost);
            menu.Items.Add(locked);
            menu.Items.Add(through);
            menu.Items.Add(Presentation.CreateWindowShadeMenu(menu));
            menu.Items.Add(topmost);
            menu.Items.Add(group);
            menu.Items.Add(Command("PinnedTitleEdit", () =>
            {
                Presentation.Title.BeginEdit();
                return Task.CompletedTask;
            }));
            var hideOthers = Command("PinnedMenuHideOthers", () =>
            {
                Presentation.HideOthersRequested?.Invoke();
                return Task.CompletedTask;
            });
            hideOthers.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(hideOthers);
            menu.Items.Add(Command("PinnedSwitchNext", () =>
            {
                Presentation.SwitchNextRequested?.Invoke();
                return Task.CompletedTask;
            }));
            menu.Opened += (_, _) =>
            {
                locked.IsChecked = IsLocked;
                through.IsChecked = IsMouseThrough;
                topmost.IsChecked = Topmost;
                hideOthers.IsChecked = Presentation.HideOthersCheckedProvider();
                through.IsEnabled = PinnedWindowPlatform.CanSetMouseThrough(this) &&
                                    (_canRestoreHidden() || IsMouseThrough);
                group.Items.Clear();
                foreach (var definition in GroupsProvider())
                    AddGroupChoice(group, definition);
                group.Items.Add(new Separator());
                var create = new MenuItem
                {
                    Tag = "new-group",
                    [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedMoveToNewGroup"),
                    InputGesture = CurrentPreferences.ShowMenuShortcuts
                        ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                                binding is { ContextKey: "BuiltinShortcutPinnedContext", Action: "new-group" })
                            ?.Gesture
                        : null,
                };
                create.Click += async (_, _) => await EditGroupAsync();
                group.Items.Add(create);
            };
        }

        private void AddGroupChoice(MenuItem parent, PinnedGroupDefinition definition)
        {
            var name = definition.Id;
            var item = new MenuItem
            {
                Header = Presentation.CreateGroupMenuHeader(definition),
                ToggleType = definition.Color is null && Group == name
                    ? MenuItemToggleType.CheckBox
                    : MenuItemToggleType.None,
                IsChecked = definition.Color is null && Group == name,
                IsEnabled = Group != name,
            };
            if (definition.Color is not null) item.Icon = PinnedGroupIcon.Create(item, definition, Group == name);
            item.Click += (_, _) =>
            {
                if (GroupChangeRequested is { } change) change(this, name);
                else SetGroup(name);
            };
            parent.Items.Add(item);
        }

        private async Task EditGroupAsync()
        {
            if (NewGroupRequested is { } create) await create(this);
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
                        throw;
                    }

                    break;
                case false:
                    ApplyMouseThrough(false);
                    if (_opacityBeforeThrough is { } restored)
                        Opacity = restored;
                    _opacityBeforeThrough = null;
                    break;
            }

            IsMouseThrough = value;
            RefreshWindowShadow();
            Presentation.ShowMouseThroughMessage(value);
            Presentation.RefreshMouseThroughToolbar();
            StateChanged?.Invoke(this, EventArgs.Empty);
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
            if (_closedState is { } closed) return closed.Copy();
            var state = new PinnedImageState
            {
                Id = Id, Group = Group, X = Position.X, Y = Position.Y, Zoom = CurrentPinZoom,
                LiveSource = SnapshotLiveDescriptor ?? LiveDescriptor,
                CaptureScaling = SnapshotLiveFrame?.NativePixelDensity ?? OriginalCaptureScaling,
                Opacity = Opacity, Locked = IsLocked, Visible = IsVisible && WindowState != WindowState.Minimized,
                ScrollX = _viewport.Offset.X, ScrollY = _viewport.Offset.Y, Topmost = Topmost,
                CreatedAtUtcTicks = _createdAtUtcTicks, Thumbnail = _isThumbnail,
                ThumbnailManualRegion = _thumbnailManualRegion,
                ThumbnailNormalZoom = _isThumbnail ? _thumbnailNormalZoom : CurrentPinZoom,
                ThumbnailSavedZoom = _isThumbnail ? CurrentPinZoom : _thumbnailSavedZoom,
                ThumbnailStandardWidth = _quickThumbnailSourceSize?.Width ?? 0,
                ThumbnailStandardHeight = _quickThumbnailSourceSize?.Height ?? 0,
                ThumbnailFullScrollX = _thumbnailFullScrollPhysical.X,
                ThumbnailFullScrollY = _thumbnailFullScrollPhysical.Y,
                ThumbnailX = _thumbnailRegion?.X ?? 0, ThumbnailY = _thumbnailRegion?.Y ?? 0,
                ThumbnailWidth = _thumbnailRegion?.Width ?? 0, ThumbnailHeight = _thumbnailRegion?.Height ?? 0,
                TextMarks = _textMarkup.Snapshot(),
                Annotations = _annotationScene?.CaptureDocument(), Orientation = _viewTransform.Orientation,
                ColorProcessing = _colorMatrixState,
                MarkHidden = IsMarkHidden,
                Translation = CaptureInlineTranslationState(),
                TransparentLayer = _transparentLayer,
                Ocr = new(_ocrTextSelectable,
                    _textRecognitionAttempted && _textRecognitionCancellation is null, _recognizedText),
            };
            Presentation.Capture(state);
            return state;
        }

        public void RestoreState(PinnedImageState state)
        {
            if (!Enum.IsDefined(state.Orientation)) throw new InvalidDataException("Invalid pinned image orientation.");
            if (state.ColorProcessing.Matrix is { IsFinite: false })
                throw new InvalidDataException("Invalid pinned image color matrix.");
            _viewTransform = new(state.Orientation);
            _colorMatrixState = state.ColorProcessing;
            _marksHidden = state.MarkHidden;
            _transparentLayer = state.TransparentLayer;
            _ocrTextSelectable = state.Ocr?.TextSelectable ?? CurrentPreferences.SelectableText;
            if (state.Ocr is { } ocr)
            {
                ResetTextRecognition();
                _recognizedText = ocr.Result;
                _textRecognitionAttempted = ocr.Attempted;
                _textSelection = new(ocr.Result is { } result
                    ? ImageTextSelectionBuilder.FromOcr(result)
                    : ImageTextSelectionDocument.Empty);
            }

            RestoreInlineTranslationState(state.Translation);
            RestoreImageProcessing();
            SetOriginalCaptureScaling(state.CaptureScaling);
            Id = state.Id;
            Group = state.Group;
            _textMarkup.Restore(state.TextMarks, new Rect(_bitmap.Size));
            if (!_textMarkup.IsEmpty) RefreshTextMarkup();
            if (state.Annotations is { } annotations) EnsureAnnotationScene().RestoreDocument(annotations);
            Presentation.Restore(state);
            _hasRestoredState = true;
            CurrentPinZoom = state.Zoom;
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
                _thumbnailManualRegion = state.ThumbnailManualRegion;
                _thumbnailNormalZoom = state.ThumbnailNormalZoom;
                _thumbnailSavedZoom = state.ThumbnailSavedZoom;
                _quickThumbnailSourceSize = state is { ThumbnailStandardWidth: > 0, ThumbnailStandardHeight: > 0 }
                    ? new(state.ThumbnailStandardWidth, state.ThumbnailStandardHeight)
                    : null;
                _thumbnailFullScrollPhysical = new(state.ThumbnailFullScrollX, state.ThumbnailFullScrollY);
            }

            UpdateSize();
            RefreshPreferencePresentation();
            Position = new(state.X, state.Y);
            _viewport.Offset = new(state.ScrollX, state.ScrollY);
            RestoreToScreen();
            if (state.LiveSource is { } live && LiveDescriptor != live)
            {
                using var initial = new WindowRoiPinFrame((int)_bitmap.Size.Width, (int)_bitmap.Size.Height,
                    _pixels, _hdrSource);
                InitializeLiveCapture(live, initial: initial);
            }

            if (!state.Visible) HideImage();
        }
    }
}
