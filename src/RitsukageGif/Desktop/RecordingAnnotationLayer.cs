using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationLayer : IDisposable
    {
        private readonly bool _autoSelectPrivacy;
        private readonly RecordingAnnotationOverlay _overlay;
        private readonly Action<AnnotationPreferences>? _persistPreferences;
        private readonly IDisposable? _popupSubscription;
        private readonly Func<AnnotationPreferences>? _preferencesProvider;
        private readonly AnnotationPropertyPresenter _properties;
        private readonly Flyout _propertyPopup;
        private readonly AnnotationToolsStrip _tools;
        private readonly Button _undo;
        private bool _disposed;
        private CapturedFrame? _latestSource;
        private PixelSize _nativeSize;
        private AnnotationPreferences _preferences;
        private ScreenshotTool? _propertyTool;
        private bool _publishQueued;
        private RecordingAnnotationSnapshot? _snapshot;
        private long _sourceRefreshAt;
        private int _sourceRefreshQueued;
        private object? _toolbarIdentity;
        private long _toolbarRevision = -1;

        internal RecordingAnnotationLayer(AnnotationPreferences preferences,
            Func<AnnotationPreferences>? preferencesProvider = null,
            Action<AnnotationPreferences>? persistPreferences = null)
        {
            _preferences = preferences;
            _preferencesProvider = preferencesProvider;
            _persistPreferences = persistPreferences;
            Session = new() { CanScroll = false, RecordingContext = true };
            Session.ApplyPreferences(preferences);
            Session.ToolbarPreferencesProvider = () => (_preferencesProvider?.Invoke() ?? _preferences).Toolbar;
            _autoSelectPrivacy = preferences.AutoSelectPrivacyMosaic;
            Session.Tool = ScreenshotTool.Select;
            _tools = new(Session, () => _ = EditToolsAsync(), true);
            _tools.RefreshShortcuts(SelectionCommands.SnapshotBindings());
            _undo = new()
            {
                Width = 30, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new(5),
                Focusable = false, Background = Brushes.Transparent, BorderThickness = new(0),
            };
            _undo.Content = ToolbarIcon.Create(_undo, ToolbarIcon.GetDefinition("AnimationUndo"));
            _undo.Bind(AutomationProperties.NameProperty, LiveText.Resource("AnimationUndo"));
            _undo.Bind(ToolTip.TipProperty, LiveText.Resource("AnimationUndo"));
            _undo.Click += (_, _) => Session.Undo();
            _properties = new(Session);
            var context = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(6), Padding = new(3),
                Child = _properties,
            };
            _propertyPopup = new()
            {
                OverlayDismissEventPassThrough = true, Placement = PlacementMode.Bottom,
                Content = new ThemeVariantScope { Child = context },
            };
            Strip = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2, Children = { _tools, _undo },
            };
            _overlay = new(Session);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                _popupSubscription = Visual.IsVisibleProperty.Changed.AddClassHandler<PopupRoot>((popup, _) =>
                {
                    if (!popup.IsVisible) return;
                    var owner = popup.ParentTopLevel;
                    while (owner is PopupRoot parent) owner = parent.ParentTopLevel;
                    if (owner != TopLevel.GetTopLevel(Strip) && !_overlay.Owns(owner)) return;
                    if (popup.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle &&
                        SetWindowDisplayAffinity(handle.Handle, 0x11)) return;
                    HideFlyouts();
                    Session.SetPrivacyNotice(UiText.Get("CaptureFailed"));
                });
            Session.Changed += Changed;
            Session.TextEditStateChanged += Changed;
            Session.ArrowCommentRequested += EditComment;
            Session.PrivacyDetectionRequested += DetectPrivacy;
            Session.ToolbarPreferencesChanged += ToolbarChanged;
            SelectionCommands.BindingsChanged += RefreshShortcuts;
            RefreshToolLayout();
            InitializeKeyboard();
        }

        internal ScreenshotAnnotationSession Session { get; }
        internal Control Strip { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Session.Changed -= Changed;
            Session.TextEditStateChanged -= Changed;
            Session.ArrowCommentRequested -= EditComment;
            Session.PrivacyDetectionRequested -= DetectPrivacy;
            Session.ToolbarPreferencesChanged -= ToolbarChanged;
            SelectionCommands.BindingsChanged -= RefreshShortcuts;
            DisposeKeyboard();
            _overlay.Dispose();
            _popupSubscription?.Dispose();
            _tools.Dispose();
            _propertyPopup.Hide();
            _propertyPopup.Content = null;
            _properties.Dispose();
            Session.Dispose();
            Volatile.Write(ref _snapshot, null);
            Volatile.Write(ref _latestSource, null);
        }

        internal event Action? BlockedToolClicked
        {
            add => _tools.BlockedToolClicked += value;
            remove => _tools.BlockedToolClicked -= value;
        }

        internal void UpdateGeometry(Window owner, PixelRect region, PixelSize nativeSize, bool visible,
            bool sourceExcludesOverlay = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            var local = new PixelRect(0, 0, region.Width, region.Height);
            var changed = _nativeSize != nativeSize || Session.Selection != local;
            _nativeSize = nativeSize;
            if (Session.Selection != local)
            {
                if (Session.IsCanvasTextEditing) Session.CommitTextEdit();
                Session.NotifySelection(local);
            }

            _overlay.Update(owner, region, visible && _hostedOwners.Count == 0, sourceExcludesOverlay);
            if (changed) PublishSnapshot();
        }

        internal RecordingAnnotationSnapshot? CaptureSnapshot()
        {
            return Volatile.Read(ref _snapshot);
        }

        private Task UpdateSourceAsync(byte[] logicalBgra, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            return Session.SetOverlayFrameAsync(Session.Selection, logicalBgra, token);
        }

        internal void OfferSourceFrame(CapturedFrame source)
        {
            if (Volatile.Read(ref _disposed) ||
                Stopwatch.GetElapsedTime(Volatile.Read(ref _sourceRefreshAt)).TotalMilliseconds < 100) return;
            if (Interlocked.CompareExchange(ref _sourceRefreshQueued, 1, 0) != 0) return;
            CapturedFrame retained;
            try
            {
                retained = source with { Pixels = source.Pixels.ToArray() };
                Volatile.Write(ref _latestSource, retained);
            }
            catch
            {
                Interlocked.Exchange(ref _sourceRefreshQueued, 0);
                throw;
            }

            _ = Dispatcher.UIThread.InvokeAsync(async Task () =>
            {
                try
                {
                    if (_disposed || Session.IsBusy || Session.IsDrawing || Session.Selection.IsEmpty ||
                        Stopwatch.GetElapsedTime(_sourceRefreshAt).TotalMilliseconds < 100) return;
                    Volatile.Write(ref _sourceRefreshAt, Stopwatch.GetTimestamp());
                    var region = Session.Selection;
                    var logical = await Task.Run(() => LogicalSource(retained, region));
                    if (_disposed || Session.Selection != region || Session.IsBusy || Session.IsDrawing) return;
                    await UpdateSourceAsync(CapturedFramePixels.ToStraightBgra(logical), CancellationToken.None);
                }
                catch (Exception error)
                {
                    if (!_disposed) Session.SetPrivacyNotice(error.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _sourceRefreshQueued, 0);
                }
            }, DispatcherPriority.Background);
        }

        private static CapturedFrame LogicalSource(CapturedFrame source, PixelRect region)
        {
            var bgra = CapturedFramePixels.ToStraightBgra(source);
            if (source.Width == region.Width && source.Height == region.Height)
                return source with { Pixels = bgra, Stride = region.Width * 4, AlphaMode = CaptureAlphaMode.Straight };
            var raster = ImagePreviewResampler.SampleSdr(bgra, source.Width, source.Height, false,
                new(0, 0, source.Width, source.Height), new(region.Width, region.Height), [], CancellationToken.None);
            var output = new byte[checked(region.Width * region.Height * 4)];
            for (var pixel = 0; pixel < region.Width * region.Height; pixel++)
            {
                var input = raster.Samples.AsSpan(pixel * 8, 8);
                var target = output.AsSpan(pixel * 4, 4);
                for (var channel = 0; channel < 3; channel++)
                    target[2 - channel] =
                        (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[(channel * 2)..]) + 128) / 257);
                target[3] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[6..]) + 128) / 257);
            }

            return source with
            {
                Width = region.Width, Height = region.Height, Stride = region.Width * 4,
                Pixels = output, AlphaMode = CaptureAlphaMode.Straight,
            };
        }

        private void DetectPrivacy(long request)
        {
            _ = DetectPrivacyAsync(request);
        }

        private async Task DetectPrivacyAsync(long request)
        {
            var token = Session.PrivacyDetectionToken;
            try
            {
                var region = Session.Selection;
                var source = Volatile.Read(ref _latestSource) ?? throw new CaptureSourceLostException();
                var frame = await Task.Run(() => LogicalSource(source, region), token);
                var service = PrivacyDetectionService.ForAnnotations(new OcrRecognitionService(),
                    new BarcodeRecognitionService(), new RecognitionPreferences().LanguageTag);
                var detected = await service.DetectAsync(frame, token);
                token.ThrowIfCancellationRequested();
                if (_disposed) return;
                var candidates = detected.Candidates.Select(candidate => candidate with
                {
                    Bounds = new(candidate.Bounds.X + region.X, candidate.Bounds.Y + region.Y,
                        candidate.Bounds.Width, candidate.Bounds.Height),
                }).ToArray();
                if (Session.SetPrivacyCandidates(request, candidates, _autoSelectPrivacy) &&
                    detected.Failures.Count > 0)
                    Session.SetPrivacyNotice(string.Join(Environment.NewLine,
                        detected.Failures.Select(failure => failure.Message)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (_disposed) return;
                Session.FailPrivacyDetection(request, error.Message);
                Session.SetPrivacyNotice(error.Message);
            }
        }

        internal void HideFlyouts()
        {
            _tools.HideFlyouts();
            _properties.HideFlyouts();
            _propertyPopup.Hide();
        }

        private void Changed()
        {
            RefreshToolLayout();
            _overlay.UpdateInput();
            _undo.IsEnabled = Session is { CanUndo: true, IsBusy: false };
            Strip.IsVisible = Session.ToolbarVisible;
            if (!Session.ToolbarVisible) HideFlyouts();
            var extent = AppTheme.CurrentAppearance.ToolbarIconSize switch
            {
                ToolbarIconSize.Small => 24, ToolbarIconSize.Large => 36, _ => 28,
            };
            _undo.Width = _undo.Height = extent;
            if (_undo.Content is Control icon) icon.Width = icon.Height = Math.Max(12, extent - 12);
            var tool = Session.SelectedTool ?? Session.Tool;
            _properties.Refresh(tool);
            if (!_properties.HasContext) _propertyPopup.Hide();
            else if (_propertyTool != tool && TopLevel.GetTopLevel(Strip) is not null)
                ShowProperties(tool);
            _propertyTool = tool;
            if (_publishQueued || _disposed) return;
            _publishQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _publishQueued = false;
                if (!_disposed) PublishSnapshot();
            }, DispatcherPriority.Render);
        }

        private void PublishSnapshot()
        {
            if (Session.Selection.IsEmpty || _nativeSize.Width <= 0 || _nativeSize.Height <= 0) return;
            Volatile.Write(ref _snapshot, Session.CreateRecordingSnapshot(_nativeSize));
        }

        private void EditComment()
        {
            _properties.EditComment();
            if (_properties.HasContext) ShowProperties(ScreenshotTool.Arrow);
        }

        private void ShowProperties(ScreenshotTool tool)
        {
            var anchor = _tools.GroupButtons.GetValueOrDefault(ScreenshotToolCatalog.CanonicalTool(tool));
            if (!_propertyPopup.IsOpen) _propertyPopup.ShowAt(anchor ?? Strip);
        }

        private void RefreshShortcuts()
        {
            _shortcutBindings = SelectionCommands.SnapshotBindings();
            _tools.RefreshShortcuts(_shortcutBindings);
        }

        private void RefreshToolLayout()
        {
            var identity = Session.ToolbarPreferencesIdentity;
            if (_toolbarRevision == Session.ToolbarRevision && ReferenceEquals(_toolbarIdentity, identity)) return;
            _toolbarRevision = Session.ToolbarRevision;
            _toolbarIdentity = identity;
            _propertyPopup.Hide();
            _tools.Rebuild(Session.ToolbarPreferences);
        }

        private void ToolbarChanged(ToolbarPreferences preferences, bool? tools)
        {
            var current = _preferencesProvider?.Invoke() ?? _preferences;
            _preferences = current with { Toolbar = current.Toolbar.Merge(preferences, tools) };
            _persistPreferences?.Invoke(_preferences);
            RefreshToolLayout();
        }

        private async Task EditToolsAsync()
        {
            if (_disposed || TopLevel.GetTopLevel(Strip) is not Window owner) return;
            var annotating = Session.CanAnnotate;
            try
            {
                HideFlyouts();
                Session.CanAnnotate = false;
                var editor = new ToolbarToolsWindow(Session.ToolbarPreferences, result =>
                {
                    if (!_disposed) Session.UpdateToolbarPreferences(result, true);
                }, Session.RecordingContext);
                PrepareNativeEditor(editor);
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    editor.Opacity = 0;
                    editor.Opened += (_, _) =>
                    {
                        if (RecordingRegionOverlay.Exclude(editor)) editor.Opacity = 1;
                        else editor.Close(null);
                    };
                }

                await editor.ShowDialog(owner);
            }
            catch (Exception error)
            {
                if (!_disposed) Session.SetPrivacyNotice(error.Message);
            }
            finally
            {
                if (!_disposed) Session.CanAnnotate = annotating;
            }
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);
    }
}
