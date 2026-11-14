using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageWindow : Window, IDisposable
    {
        private readonly Func<bool> _canRestoreHidden;
        private readonly Grid _content;
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SimulatedWheelInput _simulatedWheel;

        private readonly TextBlock _status = new()
        {
            IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new(8),
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        private readonly ScrollViewer _viewport = new()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        private ScreenshotTiledImage _bitmap;

        private bool _busy;
        private bool _closed;
        private bool _hasRestoredState;
        private bool _initialized;
        private byte[] _pixels;
        private byte[] _png;

        private Screens? _screens;

        private Bitmap _thumbnail;

        private PinnedImageWindow(byte[] png, byte[] pixels, ScreenshotTiledImage bitmap, Bitmap thumbnail,
            Func<bool> canRestoreHidden, PinnedImagePreferences preferences)
        {
            _simulatedWheel = new(this);
            _canRestoreHidden = canRestoreHidden;
            _png = png;
            _pixels = pixels;
            _bitmap = bitmap;
            _thumbnail = thumbnail;
            CurrentPreferences = preferences;
            Bind(TitleProperty, LiveText.Resource("PinnedImage"));
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            Topmost = preferences.Topmost;
            Opacity = preferences.Opacity;
            ShowInTaskbar = !_canRestoreHidden();
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            var image = _image;
            image.Source = _bitmap;
            PinnedBitmapSampling.Apply(image, preferences.SmoothScaling);
            _sceneLayer = new() { Children = { _transparentBackground, image } };
            _sceneTransform = new() { Child = _sceneLayer };
            _viewLayer = new() { Children = { _sceneTransform } };
            _viewport.Content = _viewLayer;
            _viewport.ScrollChanged += (_, _) =>
            {
                UpdateVisibleRegion();
                QueueHdrViewportRefresh();
                RefreshOcrLayerVisibility();
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            var content = _content = new() { Children = { _viewport, _status } };
            Presentation = new(this, content, () => CurrentPreferences, RunAsync, _status);
            Presentation.Title.Changed += () => StateChanged?.Invoke(this, EventArgs.Empty);
            Presentation.MoveGroupRequested = () => RunAsync(EditGroupAsync);
            Content = Presentation.Frame;
            Presentation.WindowShadowChanged += () => StateChanged?.Invoke(this, EventArgs.Empty);
            var menu = new ContextMenu();
            Presentation.ConnectMenu(menu);
            menu.Items.Add(Command("PinnedCopyImage", () => CopyMenuImageAsync(false)));
            var copyOriginal = Command("PinnedCopyOriginalImage", () => CopyMenuImageAsync(true));
            menu.Items.Add(copyOriginal);
            menu.Items.Add(Command("PinnedImageCopyFile", CopyFileAsync));
            menu.Items.Add(Command("PinnedSaveImage", SaveAsync));
            var saveOriginal = Command("PinnedOriginalImageSaveAs", SaveOriginalImageAsync);
            menu.Items.Add(saveOriginal);
            menu.Opened += (_, _) =>
            {
                copyOriginal.IsVisible = CanCaptureOriginalImage;
                saveOriginal.IsVisible = CanCaptureOriginalImage;
            };
            var annotate = Command("PinnedMenuMark", ToggleAnnotationToolbarAsync);
            annotate.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(annotate);
            var hideMark = Command("PinnedMenuHideMark", () =>
            {
                SetMarkHidden(!IsMarkHidden);
                return Task.CompletedTask;
            });
            hideMark.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(hideMark);
            menu.Opened += (_, _) =>
            {
                annotate.IsEnabled = !_busy && !IsMouseThrough;
                annotate.IsChecked = IsMarkEditing;
                hideMark.IsChecked = IsMarkHidden;
                hideMark.IsEnabled = !IsMarkEditing;
            };
            menu.Items.Add(new Separator());
            var actualSize = Command("PinnedImageActualSize", () =>
            {
                ResizeAt(new(PinnedWindowPresentation.ShadeInset, PinnedWindowPresentation.ShadeInset), 1);
                return Task.CompletedTask;
            });
            menu.Items.Add(actualSize);
            var thumbnailMode = Command("PinnedMenuThumbnail", () =>
            {
                ToggleThumbnail(new(0, 0));
                return Task.CompletedTask;
            });
            thumbnailMode.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(thumbnailMode);
            var selectThumbnailRegion = Command("PinnedImageSelectThumbnailRegion", () =>
            {
                if (!LiveCaptureActive && !IsOperationLocked(PinnedLockRestrictions.Move) && _annotation is null)
                    _regionSelectionMode = true;
                return Task.CompletedTask;
            });
            menu.Items.Add(selectThumbnailRegion);
            var selected = Command("PinnedImageMultiSelect", () =>
            {
                SelectionRequested?.Invoke(this);
                return Task.CompletedTask;
            });
            selected.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(selected);
            menu.Opened += (_, _) =>
            {
                selected.IsChecked = IsSelected;
                thumbnailMode.IsChecked = _isThumbnail;
                thumbnailMode.IsVisible = !LiveCaptureActive;
                selectThumbnailRegion.IsVisible = !LiveCaptureActive;
                thumbnailMode.IsEnabled = !IsOperationLocked(PinnedLockRestrictions.Move) && _annotation is null;
            };
            menu.Opened += (_, _) =>
                actualSize.IsEnabled = !IsOperationLocked(PinnedLockRestrictions.Move) && _annotation is null;
            AddInteractionMenu(menu);
            AddRecognitionMenu(menu);
            menu.Items.Add(new Separator());
            var hide = Command("PinnedImageHide", () =>
            {
                HideImage();
                return Task.CompletedTask;
            });
            menu.Items.Add(hide);
            menu.Opened += (_, _) => hide.Bind(HeaderedSelectingItemsControl.HeaderProperty,
                LiveText.Resource(_canRestoreHidden() ? "PinnedImageHide" : "PinnedImageMinimize"));
            menu.Items.Add(Command("Close", () =>
            {
                RequestClose();
                return Task.CompletedTask;
            }));
            menu.Items.Add(Command("PinnedImageDestroy", () =>
            {
                RequestClose(true);
                return Task.CompletedTask;
            }));
            menu.Opened += (_, _) =>
            {
                var shortcuts = BuiltinShortcuts.Snapshot();
                foreach (var item in menu.Items.OfType<MenuItem>())
                    if (item.Tag is string action)
                        item.InputGesture = CurrentPreferences.ShowMenuShortcuts
                            ? shortcuts.FirstOrDefault(binding =>
                                    binding.ContextKey == "BuiltinShortcutPinnedContext" && binding.Action == action)
                                ?.Gesture
                            : null;
            };
            AddAnimationCommands(menu);
            Presentation.AddSourceMimeCommands(menu, () => CurrentPreferences);
            Presentation.AddConfigurationCommand(menu);
            AlignSourceMenu(menu);
            AddImageSourcePrelude(menu);
            content.ContextMenu = menu;
            ConfigureMouseInput(image, content, menu);
            ConfigureQuickThumbnailPointer();
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            InitializeAnnotationKeyboard();
            Opened += (_, _) =>
            {
                if (_initialized) return;
                _initialized = true;
                _screens = Screens;
                _screens.Changed += ScreensChanged;
                var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
                if (screen is not null && preferences.AutoFit && !_hasRestoredState &&
                    _capturePlacement is null && !_hasCenteredCaptureOrigin)
                    CurrentPinZoom = Math.Min(1,
                        Math.Min(screen.WorkingArea.Width * 0.8 / _bitmap.Size.Width,
                            _bitmap.Size.Height > _bitmap.Size.Width * 4
                                ? 1
                                : screen.WorkingArea.Height * 0.8 / _bitmap.Size.Height));
                UpdateSize();
                RestoreCapturePlacement();
            };
            ScalingChanged += (_, _) => UpdateSize();
            Closed += (_, _) => ReleaseResources();
            InitializeInputRegions();
            UpdateSize();
        }

        public Func<PinnedImagePreferences>? PreferencesProvider { get; set; }
        private PinnedImagePreferences CurrentPreferences => PreferencesProvider?.Invoke() ?? field;
        public string ContentHash { get; private set; } = "";

        public void Dispose()
        {
            if (_closed) return;
            try
            {
                Close();
            }
            finally
            {
                ReleaseResources();
            }
        }

        public PinnedWindowPresentation Presentation { get; }

        public void HideImage()
        {
            Presentation.SetUserVisibility(false);
            var wasVisible = IsVisible && WindowState != WindowState.Minimized;
            if (_canRestoreHidden())
            {
                Hide();
            }
            else
            {
                ShowInTaskbar = true;
                WindowState = WindowState.Minimized;
            }

            if (wasVisible) PinLogger.ZLogInformation($"Pinned image hidden.");
        }

        public static PinnedImageWindow CreatePrepared(PinnedImageContent content, Func<bool> canRestoreHidden,
            PinnedImagePreferences? preferences = null)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(canRestoreHidden);
            preferences = (preferences ?? new()).Normalize();
            var bitmap = new ScreenshotTiledImage(content.Width, content.Height, content.Pixels, AlphaFormat.Unpremul);
            Bitmap? thumbnail = null;
            try
            {
                using var stream = new MemoryStream(content.ThumbnailPng, false);
                thumbnail = new(stream);
                var window = new PinnedImageWindow(content.Png, content.Pixels, bitmap, thumbnail, canRestoreHidden,
                    preferences);
                try
                {
                    window.ConfigureHdr(content.Hdr);
                    window._sdrPng = content.SdrPng;
                    window.InitializeOriginal(content);
                    window.InitializeAnimation(content);
                    window.ContentHash = content.ContentHash;
                    window.RefreshPreferencePresentation();
                    return window;
                }
                catch
                {
                    window.Dispose();
                    throw;
                }
            }
            catch
            {
                thumbnail?.Dispose();
                bitmap.Dispose();
                throw;
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _annotation?.CommitTextEdit();
            _closedState = CaptureState();
        }

        private void ReleaseResources()
        {
            if (_closed) return;
            _annotation?.CommitTextEdit();
            _closedState ??= CaptureState();
            _animationActiveAtClose = AnimationActive;
            _closed = true;
            _inputRegions?.Dispose();
            _inputRegions = null;
            CancelThumbnailLayout();
            DisposeLiveCapture();
            DisposeAnimation();
            if (_screens is not null) _screens.Changed -= ScreensChanged;
            _inlineTranslation?.Dispose();
            _lifetime.Cancel();
            ResetTextRecognition();
            ClearTextMarkup();
            _textSelectionMenu?.Dispose();
            _textSelectionMenu = null;
            _hdrPreviewWork?.Cancel();
            EndAnnotation();
            _mouseClicks.Dispose();
            _image.Source = null;
            _hdrPreview?.Dispose();
            _bitmap.Dispose();
            _thumbnail.Dispose();
            if (!_busy) _lifetime.Dispose();
        }

        private void RestoreToScreen()
        {
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;
            var width = (int)Math.Ceiling(Width * RenderScaling);
            var height = (int)Math.Ceiling(Height * RenderScaling);
            Position = new(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }

        private void ScreensChanged(object? sender, EventArgs args)
        {
            UpdateSize();
            RestoreToScreen();
        }

        private void UpdateSize()
        {
            if (_closed) return;
            RefreshOcrLayerVisibility();
            var display = DisplayRegion;
            _image.Width = Math.Max(1, display.Width * CurrentPinZoom / RenderScaling);
            _image.Height = Math.Max(1, display.Height * CurrentPinZoom / RenderScaling);
            _sceneLayer.Width = _image.Width;
            _sceneLayer.Height = _image.Height;
            var sourceSize = new Size(_image.Width, _image.Height);
            _sceneTransform.LayoutTransform = new MatrixTransform(_viewTransform.MatrixFor(sourceSize));
            var viewSize = _viewTransform.ViewSize(sourceSize);
            _viewLayer.Width = viewSize.Width;
            _viewLayer.Height = viewSize.Height;
            if (_annotationScene is not null)
            {
                _annotationSurface?.Scale = RenderScaling / CurrentPinZoom;
                _annotationSurface?.SourceOrigin = new(display.X, display.Y);
            }

            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            var maximum = PinnedViewportGeometry.MaximumViewport(CurrentPreferences.MaximumWindowSize,
                RenderScaling,
                new(screen?.WorkingArea.Width ?? 1280, screen?.WorkingArea.Height ?? 720));
            Width = Math.Min(viewSize.Width, maximum.Width) + PinnedWindowPresentation.ShadeInset * 2;
            Height = Math.Min(viewSize.Height + (_hdrSource is null ? 0 : 20), maximum.Height) +
                     PinnedWindowPresentation.ShadeInset * 2;
            RefreshTransparentBackground();
            RefreshWindowShadow();
            UpdateVisibleRegion();
            QueueHdrViewportRefresh();
        }

        private void UpdateVisibleRegion()
        {
            if (_closed) return;
            var scale = CurrentPinZoom / RenderScaling;
            var size = _bitmap.Size;
            var display = DisplayRegion;
            if (_annotationScene is not null && _annotationPreviewImage is { } preview)
            {
                if (_isThumbnail)
                {
                    _croppedImage ??= new(preview, display);
                    _croppedImage.Source = preview;
                    _croppedImage.Region = display;
                    _image.Source = _croppedImage;
                }
                else
                {
                    _image.Source = preview;
                }
            }
            else if (LiveCaptureActive)
            {
                UpdateLiveVisibleRegion(display);
            }
            else if (AnimationActive && _animationSurface is { } animation)
            {
                animation.Region = display;
                _image.Source = animation;
            }
            else if (_isThumbnail)
            {
                _croppedImage ??= new(_bitmap, display);
                _croppedImage.Source = _bitmap;
                _croppedImage.Region = display;
                _image.Source = _croppedImage;
            }
            else
            {
                _image.Source = size.Width * CurrentPinZoom <= _thumbnail.PixelSize.Width + 1 &&
                                size.Height * CurrentPinZoom <= _thumbnail.PixelSize.Height + 1
                    ? _thumbnail
                    : _bitmap;
            }

            var visible = _viewTransform.SourceBounds(new(_viewport.Offset.X / scale, _viewport.Offset.Y / scale,
                    Math.Max(1, _viewport.Viewport.Width) / scale, Math.Max(1, _viewport.Viewport.Height) / scale),
                new(display.Width, display.Height));
            _bitmap.VisibleRegion = visible.Translate(new(display.X, display.Y));
            if (_annotationPreviewImage is { } image) image.VisibleRegion = _bitmap.VisibleRegion;
            _image.InvalidateVisual();
            InvalidateInputUi();
            RefreshOcrLayerVisibility();
        }

        private void ResizeAt(Point anchor, double zoom)
        {
            if (IsOperationLocked(PinnedLockRestrictions.Move) || _annotation is not null) return;
            if (!double.IsFinite(zoom)) return;
            var next = Math.Clamp(zoom,
                Math.Min(1,
                    Math.Min(24d / Math.Min(_bitmap.Size.Width, _bitmap.Size.Height),
                        2048d / Math.Max(_bitmap.Size.Width, _bitmap.Size.Height))),
                _isThumbnail ? double.MaxValue : 8);
            var ratio = next / CurrentPinZoom;
            var location = Position;
            var offset = _viewport.Offset;
            const double inset = PinnedWindowPresentation.ShadeInset;
            anchor = new(anchor.X - inset, anchor.Y - inset);
            var oldWidth = Width - inset * 2;
            var oldHeight = Height - inset * 2;
            CurrentPinZoom = next;
            UpdateSize();
            _viewport.Offset = new(
                Math.Max(0, (offset.X + anchor.X) * ratio - anchor.X * (Width - inset * 2) / oldWidth),
                Math.Max(0, (offset.Y + anchor.Y) * ratio - anchor.Y * (Height - inset * 2) / oldHeight));
            Position = new(
                location.X + (int)Math.Round(anchor.X * RenderScaling * (1 - (Width - inset * 2) / oldWidth)),
                location.Y + (int)Math.Round(anchor.Y * RenderScaling * (1 - (Height - inset * 2) / oldHeight)));
        }

        private MenuItem Command(string key, Func<Task> action)
        {
            var item = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(key),
                Tag = key switch
                {
                    "PinnedCopyImage" => "copy", "PinnedSaveImage" => "save", "Close" => "close",
                    "PinnedCopyOriginalImage" => "copy-original", "PinnedOriginalImageSaveAs" => "save-original",
                    "PinnedMenuThumbnail" => "thumbnail", "PinnedImageDestroy" => "destroy",
                    "PinnedImageHide" => "hide", "PinnedImageAnnotate" or "PinnedMenuMark" => "annotate",
                    "PinnedMenuHideMark" => "hide-mark",
                    "PinnedTitleEdit" => "set-title", "PinnedMenuHideOthers" => "hide-others",
                    "PinnedSwitchNext" => "switch-next", _ => null,
                },
            };
            item.Click += async (_, _) => await Presentation.RunMenuAsync(() => RunAsync(action));
            return item;
        }

        private void OnKeyDown(object? sender, KeyEventArgs args)
        {
            if (_annotationToolbar?.HandleAnnotationMenuKey(args, true) == true) return;
            if (args.Handled || BuiltinShortcutTextInput.OwnsKey(args) || IsAnnotationTextInputKey(args)) return;
            if (HasTextSelection && args is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
            {
                ClearTextSelection();
                args.Handled = true;
                return;
            }

            if (_regionSelectionMode && args is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
            {
                _regionSelectionMode = false;
                if (_regionOutline is not null) _content.Children.Remove(_regionOutline);
                RefreshHdrPreview();
                args.Handled = true;
                return;
            }

            if (_annotation is not null)
            {
                AnnotationKeyDown(args);
                return;
            }

            var bindings = BuiltinShortcuts.Snapshot();
            var action = BuiltinShortcuts.Resolve(bindings, CurrentPinShortcutContext, args) ??
                         BuiltinShortcuts.Resolve(bindings, BuiltinShortcutContexts.Pinned, args);
            if (action is null) return;
            using var input = PinnedInputContext.Use(PinnedInputContext.Current | PinnedInputOrigin.Keyboard);
            args.Handled = true;
            if (TryExecutePinSubtypeShortcut(action) || ExecuteMultiSelectionShortcut(action) ||
                Presentation.Execute(action)) return;
            if (ParseTransformAction(action) is { } transform)
            {
                if (CanTransformImage(transform))
                    _ = RunAsync(() => ApplyImageTransformAsync(transform, _lifetime.Token));
                return;
            }

            switch (action)
            {
                case "menu": Presentation.OpenMenu(_content.ContextMenu!, _content); break;
                case "wheel-up": _simulatedWheel.Raise(1); break;
                case "wheel-down": _simulatedWheel.Raise(-1); break;
                case "hide": HideImage(); break;
                case "close": RequestClose(); break;
                case "copy-plain-text":
                    _ = RunAsync(() => Presentation.CopySourceMimeAsync(PinnedSourceMimeFormat.PlainText));
                    break;
                case "copy-html":
                    _ = RunAsync(() => Presentation.CopySourceMimeAsync(PinnedSourceMimeFormat.Html));
                    break;
                case "copy": _ = RunAsync(HasTextSelection ? CopySelectedTextAsync : CopyAsync); break;
                case "save": _ = RunAsync(SaveAsync); break;
                case "quick-save": _ = RunAsync(QuickSaveAsync); break;
                case "print" when ScreenshotPrintService.IsSupported: _ = RunAsync(PrintDocumentAsync); break;
                case "annotate": _ = RunAsync(BeginAnnotationAsync); break;
                case "hide-mark": SetMarkHidden(!IsMarkHidden); break;
                case "annotate-toolbar": _ = RunAsync(ToggleAnnotationToolbarAsync); break;
                case "brightness-increase":
                    _ = RunAsync(() =>
                        ApplyImageProcessingAsync(_colorMatrixState.AdjustBrightness(.1f), _lifetime.Token));
                    break;
                case "brightness-decrease":
                    _ = RunAsync(() =>
                        ApplyImageProcessingAsync(_colorMatrixState.AdjustBrightness(-.1f), _lifetime.Token));
                    break;
                case "opacity-increase": AdjustOpacity(.1); break;
                case "opacity-decrease": AdjustOpacity(-.1); break;
                case "reset-processing": _ = RunAsync(ResetImageProcessingAsync); break;
                case "select-all-ocr" when CanSelectImageText:
                    if (ActiveTextSelection.SelectAll())
                    {
                        ShowTextSelection();
                        ShowTextSelectionMenu();
                    }

                    break;
                case "ocr-text-selectable": _ = RunAsync(ToggleOcrTextSelectableAsync); break;
                case "recognize-barcode": _ = RunAsync(() => RecognizeAsync(RecognitionMode.Barcode)); break;
                case "recognize-table": _ = RunAsync(() => RecognizeAsync(RecognitionMode.Table)); break;
                case "recognize-formula": _ = RunAsync(() => RecognizeAsync(RecognitionMode.Formula)); break;
                case "translate-inline" when HasInlineTranslationBackend || _inlineTranslation?.Cached is not null:
                    _ = RunAsync(ToggleInlineTranslationAsync);
                    break;
                case "copy-all-text": _ = RunAsync(CopyCurrentRecognizedTextAsync); break;
                case "copy-file": _ = RunAsync(CopyFileAsync); break;
                case "destroy": RequestClose(true); break;
                case "thumbnail": ToggleThumbnail(); break;
                case "select-roi" when !LiveCaptureActive && !IsOperationLocked(PinnedLockRestrictions.Move):
                    ClearTextSelection();
                    _regionSelectionMode = true;
                    break;
                case "restore-roi" when _isThumbnail: ToggleThumbnail(); break;
                case "reset": ResetImage(_viewport.Bounds.Center); break;
                case "select": SelectionRequested?.Invoke(this); break;
                case "move-up": NudgeWindow(0, -1); break;
                case "move-down": NudgeWindow(0, 1); break;
                case "move-left": NudgeWindow(-1, 0); break;
                case "move-right": NudgeWindow(1, 0); break;
                case "lock": SetLocked(!IsLocked); break;
                case "topmost": SetTopmost(!Topmost); break;
                case "through" when PinnedWindowPlatform.CanSetMouseThrough(this) &&
                                    (_canRestoreHidden() || IsMouseThrough):
                    _ = RunAsync(() =>
                    {
                        SetMouseThrough(!IsMouseThrough);
                        return Task.CompletedTask;
                    });
                    break;
            }
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_closed || _busy) return;
            _busy = true;
            _status.IsVisible = false;
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                PinLogger.ZLogDebug($"Pinned image operation cancelled.");
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image operation failed.");
                if (!_closed)
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
            finally
            {
                _busy = false;
                _annotation?.SetExportState(false);
                if (_closed) _lifetime.Dispose();
            }
        }

        private async Task CopyAsync()
        {
            var output = await CapturePreferredOutputAsync(OutputColorModeProvider(), _lifetime.Token);
            await CopyImageAsync(output);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
        }

        private async Task SaveAsync()
        {
            await SaveOutputAsync(await CapturePreferredOutputAsync(OutputColorModeProvider(), _lifetime.Token));
        }
    }
}
