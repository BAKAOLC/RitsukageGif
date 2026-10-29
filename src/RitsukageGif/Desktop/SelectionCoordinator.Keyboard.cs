using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private readonly HashSet<Key> _selectionKeys = [];
        private readonly HashSet<Key> _textInputKeys = [];
        private bool _shiftKeyHeld;
        private bool _shiftTapPending;
        private bool _shiftUsedAsModifier;

        internal IReadOnlyList<SelectionCommandDefinition> ShortcutBindings { get; } =
            SelectionCommands.SnapshotBindings();

        internal SelectionHintMode HintMode { get; private set; }
        internal FrozenScreenshotCursor? ScreenshotCursor { get; private set; }

        internal bool CanToggleCursor =>
            !UsesLocalSource && (!ScreenshotMode || OperatingSystem.IsWindows());

        private void CancelModifierTap()
        {
            _shiftTapPending = false;
        }

        internal void ExecuteToolbarCommand(SelectionCommand command)
        {
            if (command == SelectionCommand.Cancel)
            {
                Cancel();
                return;
            }

            if (_finished || _pendingFinish || _screenshotCompleting) return;
            ExecuteSelectionCommand(new(command, Key.None, KeyModifiers.None, string.Empty));
            Refresh();
        }

        private void ReleaseTextInputKey(Key key)
        {
            CancelModifierTap();
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (key)
            {
                case Key.LeftAlt or Key.RightAlt: AuxiliaryVisible = false; break;
                case Key.LeftShift or Key.RightShift: _shiftKeyHeld = false; break;
            }

            ReleaseSelectionKey(key);
        }

        internal bool HandleTextInputKey(Key key, bool down, bool textBoxFocused = false)
        {
            if (!textBoxFocused && Annotations is not { IsEditingText: true } && !_textInputKeys.Contains(key))
                return false;
            if (down)
            {
                _textInputKeys.Add(key);
                CancelModifierTap();
            }
            else
            {
                _textInputKeys.Remove(key);
                ReleaseTextInputKey(key);
            }

            return true;
        }

        private void ReleaseSelectionKey(Key key)
        {
            _selectionKeys.Remove(key);
            _enter = _confirmationKey is { } confirm && _selectionKeys.Contains(confirm);
            _escape = _cancellationKey is { } cancel && _selectionKeys.Contains(cancel);
            ResumeAutoConfirm();
        }

        private void HandleSelectionKey(Key key, bool down, KeyModifiers modifiers)
        {
            if (key is Key.LeftShift or Key.RightShift)
            {
                if (down)
                {
                    if (!_shiftKeyHeld) _shiftTapPending = modifiers == KeyModifiers.Shift && !_mouseInputHeld;
                    _shiftKeyHeld = true;
                }
                else
                {
                    _shiftKeyHeld = false;
                    var execute = _shiftTapPending && !_shiftUsedAsModifier && modifiers == KeyModifiers.None;
                    _shiftTapPending = false;
                    if (execute && SelectionCommands.TryResolve(Key.LeftShift, KeyModifiers.None, out var shiftCommand,
                            ShortcutBindings) && shiftCommand is not null)
                        ExecuteSelectionCommand(shiftCommand);
                }

                return;
            }

            if (!down) return;
            _shiftTapPending = false;

            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                _shiftUsedAsModifier = true;
                _shiftTimer?.Dispose();
                _shiftTimer = null;
            }

            if (!SelectionCommands.TryResolve(key, modifiers, out var definition, ShortcutBindings) ||
                definition is null)
                return;
            var first = _selectionKeys.Add(key);
            if (!first && !definition.Repeat) return;
            ExecuteSelectionCommand(definition, true);
        }

        private void ExecuteSelectionCommand(SelectionCommandDefinition definition, bool keyboard = false)
        {
            if (definition.Command == SelectionCommand.Confirm &&
                Annotations is { IsBusy: false, IsDrawingPolyline: true } polyline)
            {
                polyline.FinishPolyline();
                return;
            }

            if (HandleAnnotationCommand(definition)) return;
            var snapshot = Snapshot;
            if (!SelectionCommands.IsAvailable(definition.Command, snapshot, CanDetectControls, CanSwitchSource,
                    CanToggleCursor, Annotations is { IsDrawing: true }, ScreenshotMode,
                    ScreenshotDrawing)) return;
            switch (definition.Command)
            {
                case SelectionCommand.SelectScreenOrAll: SelectScreenOrAll(); break;
                case SelectionCommand.PreviousRegion: BrowseRegionHistory(true); break;
                case SelectionCommand.NextRegion: BrowseRegionHistory(false); break;
                case SelectionCommand.PreviousScreenshot: BrowseImageHistory(true); break;
                case SelectionCommand.NextScreenshot: BrowseImageHistory(false); break;
                case SelectionCommand.RefreshScreenshot: RefreshScreenshot(); break;
                case SelectionCommand.MovePointer: MovePointer(definition.DirectionKey); break;
                case SelectionCommand.Confirm:
                    if (keyboard) _enter = true;
                    Confirm();
                    break;
                case SelectionCommand.Cancel:
                    if (keyboard) _escape = true;
                    if (CancelSelectionManipulation()) ReleaseCaptures();
                    else if (_state.Selected.IsEmpty && !_state.Dragging) RequestFinish(null);
                    else ClearSelection();
                    break;
                case SelectionCommand.Clear:
                    ClearSelection();
                    break;
                case SelectionCommand.Undo:
                    Annotations?.Undo();
                    break;
                case SelectionCommand.Redo:
                    Annotations?.Redo();
                    break;
                case SelectionCommand.Copy:
                    Annotations?.RequestComplete(ScreenshotOutputAction.Copy);
                    break;
                case SelectionCommand.Save:
                    Annotations?.RequestComplete(ScreenshotOutputAction.Save);
                    break;
                case SelectionCommand.Pin:
                    Annotations?.RequestComplete(ScreenshotOutputAction.Pin);
                    break;
                case SelectionCommand.ScrollVertical:
                case SelectionCommand.ScrollHorizontal:
                    Annotations?.RequestScrolling(definition.Command == SelectionCommand.ScrollVertical);
                    break;
                case SelectionCommand.RecognizeText:
                    RecognizeSelection(RecognitionMode.Text);
                    break;
                case SelectionCommand.RecognizeTextAndCopy:
                    RecognizeSelection(RecognitionMode.Text, true);
                    break;
                case SelectionCommand.RecognizeQr:
                    RecognizeSelection(RecognitionMode.Barcode);
                    break;
                case SelectionCommand.CopyColor:
                    _ = CopySampleColorAsync();
                    break;
                case SelectionCommand.CycleColorFormat:
                    SampleColorFormat = SampleColorText.Next(SampleColorFormat);
                    break;
                case SelectionCommand.ToggleAnnotations: Annotations?.ToggleAnnotations(); break;
                case SelectionCommand.ClearAnnotations: Annotations?.ClearAnnotations(); break;
                case SelectionCommand.RenumberAnnotations: Annotations?.RenumberAnnotations(); break;
                case SelectionCommand.FinishPolyline: Annotations?.FinishPolyline(); break;
                case SelectionCommand.SelectTool:
                    if (Annotations is { IsBusy: false } annotations && definition.Tool is { } tool)
                        annotations.Tool = tool;
                    break;
                case SelectionCommand.ToggleControls:
                    ToggleControls();
                    break;
                case SelectionCommand.ToggleSource:
                    ToggleSource();
                    break;
                case SelectionCommand.ToggleCursor:
                    _cursor = !_cursor;
                    PreferencesChanged?.Invoke();
                    break;
                case SelectionCommand.CycleHints:
                    HintMode = HintMode switch
                    {
                        SelectionHintMode.Full => SelectionHintMode.Compact,
                        SelectionHintMode.Compact => SelectionHintMode.Hidden,
                        _ => SelectionHintMode.Full,
                    };
                    PreferencesChanged?.Invoke();
                    break;
                case SelectionCommand.CycleSnapping:
                    SnappingMode = (SnappingMode + 1) % 4;
                    PreferencesChanged?.Invoke();
                    break;
                case SelectionCommand.Move:
                case SelectionCommand.Expand:
                case SelectionCommand.Shrink:
                    var limits = WindowSource && !UsesLocalSource
                        ? snapshot.Target?.Client ?? default
                        : SelectionBounds();
                    if (limits.IsEmpty) break;
                    var region = SelectionCommands.AdjustRegion(snapshot.Region, limits, definition.Command,
                        definition.DirectionKey);
                    if (region == snapshot.Region) break;
                    _selectedTarget = snapshot.Target;
                    _selectionIsWholeTarget = false;
                    _state.Set(region);
                    break;
                default:
                    return;
            }
        }

        private PixelRect SelectionBounds()
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;
            foreach (var window in _windows)
            {
                left = Math.Min(left, window.Region.X);
                top = Math.Min(top, window.Region.Y);
                right = Math.Max(right, window.Region.Right);
                bottom = Math.Max(bottom, window.Region.Bottom);
            }

            if (_windows.Count == 0)
                foreach (var screen in _screens.All)
                {
                    left = Math.Min(left, screen.Bounds.X);
                    top = Math.Min(top, screen.Bounds.Y);
                    right = Math.Max(right, screen.Bounds.Right);
                    bottom = Math.Max(bottom, screen.Bounds.Bottom);
                }

            return right <= left || bottom <= top
                ? default
                : new(left, top, checked(right - left), checked(bottom - top));
        }
    }
}
