using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageWindow : Window, IDisposable
    {
        private readonly Func<bool> _canRestoreHidden;
        private readonly Grid _content;
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly CancellationTokenSource _lifetime = new();

        private readonly TextBlock _status = new()
        {
            IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new(8),
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        private readonly ScrollViewer _viewport = new()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        private ScreenshotTiledImage _bitmap;

        private bool _busy;
        private bool _closed;
        private bool _initialized;
        private byte[] _pixels;
        private byte[] _png;

        private Screens? _screens;

        private Bitmap _thumbnail;
        private double _zoom = 1;

        private PinnedImageWindow(byte[] png, byte[] pixels, ScreenshotTiledImage bitmap, Bitmap thumbnail,
            Func<bool> canRestoreHidden, PinnedImagePreferences preferences)
        {
            _canRestoreHidden = canRestoreHidden;
            _png = png;
            _pixels = pixels;
            _bitmap = bitmap;
            _thumbnail = thumbnail;
            CurrentPreferences = preferences;
            Bind(TitleProperty, LiveText.Resource("PinnedImage"));
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            Topmost = preferences.Topmost;
            Opacity = preferences.Opacity;
            ShowInTaskbar = !_canRestoreHidden();
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = AppTheme.Workspace;
            var image = _image;
            image.Source = _bitmap;
            _viewport.Content = image;
            _viewport.ScrollChanged += (_, _) =>
            {
                UpdateVisibleRegion();
                _textSelectionSurface?.InvalidateVisual();
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            var content = _content = new() { Children = { _viewport, _status } };
            Content = content;
            image.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedImageHelp"));
            var menu = new ContextMenu();
            menu.Items.Add(Command("ScreenshotCopy", CopyAsync));
            menu.Items.Add(Command("PinnedImageCopyFile", CopyFileAsync));
            menu.Items.Add(Command("ScreenshotSave", SaveAsync));
            var annotate = Command("PinnedImageAnnotate", BeginAnnotationAsync);
            menu.Items.Add(annotate);
            menu.Items.Add(Command("PinnedImageAnnotationToolbar", ToggleAnnotationToolbarAsync));
            menu.Opened += (_, _) =>
                annotate.IsEnabled = !IsOperationLocked(PinnedLockedOperations.Annotation) && !_busy &&
                                     _annotation is null;
            menu.Items.Add(new Separator());
            var actualSize = Command("PinnedImageActualSize", () =>
            {
                ResizeAt(new(0, 0), 1);
                return Task.CompletedTask;
            });
            menu.Items.Add(actualSize);
            menu.Items.Add(Command("PinnedImageCopyText", CopyRecognizedTextAsync));
            var thumbnailMode = Command("PinnedImageThumbnail", () =>
            {
                ToggleThumbnail();
                return Task.CompletedTask;
            });
            thumbnailMode.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(thumbnailMode);
            menu.Items.Add(Command("PinnedImageSelectThumbnailRegion", () =>
            {
                if (!IsOperationLocked(PinnedLockedOperations.Resize) && _annotation is null)
                    _regionSelectionMode = true;
                return Task.CompletedTask;
            }));
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
                thumbnailMode.IsEnabled = !IsOperationLocked(PinnedLockedOperations.Resize) && _annotation is null;
            };
            menu.Opened += (_, _) =>
                actualSize.IsEnabled = !IsOperationLocked(PinnedLockedOperations.Resize) && _annotation is null;
            AddMultiSelectionMenu(menu);
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
            content.ContextMenu = menu;
            ConfigureMouseInput(image, content, menu);
            KeyDown += OnKeyDown;
            InitializeAnnotationKeyboard();
            Opened += (_, _) =>
            {
                if (_initialized) return;
                _initialized = true;
                _screens = Screens;
                _screens.Changed += ScreensChanged;
                var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
                if (screen is not null && preferences.AutoFit && _capturePlacement is null)
                    _zoom = Math.Min(1,
                        Math.Min(screen.WorkingArea.Width * 0.8 / _bitmap.Size.Width,
                            _bitmap.Size.Height > _bitmap.Size.Width * 4
                                ? 1
                                : screen.WorkingArea.Height * 0.8 / _bitmap.Size.Height));
                UpdateSize();
                RestoreCapturePlacement();
            };
            ScalingChanged += (_, _) => UpdateSize();
            Closed += (_, _) => ReleaseResources();
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

        private void ReleaseResources()
        {
            if (_closed) return;
            _closed = true;
            if (_screens is not null) _screens.Changed -= ScreensChanged;
            _lifetime.Cancel();
            ResetTextRecognition();
            _hdrPreviewWork?.Cancel();
            EndAnnotation();
            _mouseClicks.Dispose();
            _image.Source = null;
            _hdrPreview?.Dispose();
            _bitmap.Dispose();
            _thumbnail.Dispose();
            if (!_busy) _lifetime.Dispose();
        }

        public void HideImage()
        {
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

        public void RestoreToScreen()
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
            _textSelectionSurface?.InvalidateVisual();
            var display = DisplayRegion;
            _image.Width = Math.Max(1, display.Width * _zoom / RenderScaling);
            _image.Height = Math.Max(1, display.Height * _zoom / RenderScaling);
            if (_hdrLayer is not null)
            {
                _hdrLayer.Width = _image.Width;
                _hdrLayer.Height = _image.Height;
            }

            if (_annotation is not null)
            {
                _annotationSurface?.Scale = RenderScaling / _zoom;
                if (_viewport.Content is Grid layer)
                {
                    layer.Width = _image.Width;
                    layer.Height = _image.Height;
                }

                UpdateVisibleRegion();
                return;
            }

            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            Width = Math.Min(_image.Width, (screen?.WorkingArea.Width ?? 1280) * 0.9 / RenderScaling);
            Height = Math.Min(_image.Height + (_hdrSource is null ? 0 : 20),
                (screen?.WorkingArea.Height ?? 720) * 0.9 / RenderScaling);
            UpdateVisibleRegion();
        }

        private void UpdateVisibleRegion()
        {
            if (_closed) return;
            var scale = _zoom / RenderScaling;
            var size = _bitmap.Size;
            var display = DisplayRegion;
            if (_annotation is not null && _annotationPreviewImage is { } preview)
            {
                _image.Source = preview;
            }
            else if (_isThumbnail)
            {
                _croppedImage ??= new(_bitmap, display);
                _croppedImage.Region = display;
                _image.Source = _croppedImage;
            }
            else
            {
                _image.Source = size.Width * _zoom <= _thumbnail.PixelSize.Width + 1 &&
                                size.Height * _zoom <= _thumbnail.PixelSize.Height + 1
                    ? _thumbnail
                    : _bitmap;
            }

            _bitmap.VisibleRegion = new(display.X + _viewport.Offset.X / scale, display.Y + _viewport.Offset.Y / scale,
                Math.Max(1, _viewport.Viewport.Width) / scale, Math.Max(1, _viewport.Viewport.Height) / scale);
            if (_annotationPreviewImage is { } image) image.VisibleRegion = _bitmap.VisibleRegion;
            _image.InvalidateVisual();
            _textSelectionSurface?.InvalidateVisual();
        }

        private void ResizeAt(Point anchor, double zoom)
        {
            if (IsOperationLocked(PinnedLockedOperations.Resize) || _annotation is not null) return;
            var next = Math.Clamp(zoom,
                Math.Min(1,
                    Math.Min(24d / Math.Min(_bitmap.Size.Width, _bitmap.Size.Height),
                        2048d / Math.Max(_bitmap.Size.Width, _bitmap.Size.Height))),
                8);
            var ratio = next / _zoom;
            var location = Position;
            var offset = _viewport.Offset;
            var oldWidth = Width;
            var oldHeight = Height;
            _zoom = next;
            UpdateSize();
            _viewport.Offset = new(Math.Max(0, (offset.X + anchor.X) * ratio - anchor.X * Width / oldWidth),
                Math.Max(0, (offset.Y + anchor.Y) * ratio - anchor.Y * Height / oldHeight));
            Position = new(location.X + (int)Math.Round(anchor.X * RenderScaling * (1 - Width / oldWidth)),
                location.Y + (int)Math.Round(anchor.Y * RenderScaling * (1 - Height / oldHeight)));
        }

        private MenuItem Command(string key, Func<Task> action)
        {
            var item = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(key),
                Tag = key switch
                {
                    "ScreenshotCopy" => "copy", "ScreenshotSave" => "save", "Close" => "close",
                    "PinnedImageHide" => "hide", "PinnedImageAnnotate" => "annotate", _ => null,
                },
            };
            var geometry = key switch
            {
                "ScreenshotCopy" or "ScreenshotSave" => ToolbarIcon.Geometry(key),
                "PinnedImageCopyFile" => ToolbarIcon.Geometry("EditorNew"),
                "PinnedImageAnnotate" => ToolbarIcon.Geometry("AnimationBrush"),
                "PinnedImageActualSize" => ToolbarIcon.Geometry("AnimationActualSize"),
                "PinnedImageThumbnail" => "M3,4 H21 V20 H3 Z M3,14 L8,9 13,15 17,11 21,16",
                "PinnedImageSelectThumbnailRegion" => "M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15",
                "PinnedImageMultiSelect" => "M3,3 H11 V11 H3 Z M13,13 H21 V21 H13 Z M5,7 L7,9 10,5",
                "PinnedImageDestroy" => "M4,6 H20 M9,3 H15 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17",
                "PinnedImageCopyText" => "M3,5 H16 M9,5 V19 M5,19 H13 M18,11 H22 V22 H12 V20",
                "PinnedImageHide" => "M3,12 H21", "Close" => "M4,4 L20,20 M20,4 L4,20",
                "RecognizeText" => "M3,5 H21 M12,5 V21 M8,21 H16",
                "RecognizeBarcode" => "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z",
                _ => null,
            };
            if (geometry is not null)
                item.Icon = new Path
                {
                    Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                    StrokeThickness = 1.6,
                    [!Shape.StrokeProperty] = item[!ForegroundProperty],
                };
            item.Click += async (_, _) => await RunAsync(action);
            return item;
        }

        private void OnKeyDown(object? sender, KeyEventArgs args)
        {
            if (args.Source is TextBox || IsAnnotationTextInputKey(args.Key)) return;
            if (_selectedTextStart >= 0 && args is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
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

            var action = BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedContext", args);
            if (IsOperationLocked(PinnedLockedOperations.Shortcuts) && action is not (null or "lock" or "menu"))
            {
                args.Handled = true;
                return;
            }

            if (action is null or "menu") return;
            args.Handled = true;
            if (ExecuteMultiSelectionShortcut(action)) return;
            switch (action)
            {
                case "hide": HideImage(); break;
                case "close": RequestClose(); break;
                case "copy": _ = RunAsync(_selectedTextStart >= 0 ? CopySelectedTextAsync : CopyAsync); break;
                case "save": _ = RunAsync(SaveAsync); break;
                case "annotate": _ = RunAsync(BeginAnnotationAsync); break;
                case "annotate-toolbar": _ = RunAsync(ToggleAnnotationToolbarAsync); break;
                case "copy-all-text": _ = RunAsync(CopyRecognizedTextAsync); break;
                case "copy-file": _ = RunAsync(CopyFileAsync); break;
                case "destroy": RequestClose(true); break;
                case "thumbnail": ToggleThumbnail(); break;
                case "select-roi" when !IsOperationLocked(PinnedLockedOperations.Resize):
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
                case "through" when OperatingSystem.IsWindows() && (_canRestoreHidden() || IsMouseThrough):
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
            var output = await CaptureOutputAsync(OutputColorModeProvider(), _lifetime.Token);
            await CopyImageAsync(output);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
        }

        private async Task SaveAsync()
        {
            await SaveOutputAsync(await CaptureOutputAsync(OutputColorModeProvider(), _lifetime.Token));
        }
    }
}
