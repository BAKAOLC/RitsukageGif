using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private CancellationTokenSource? _selectionCancellation;
        private Window? _selectionDialog;
        private Task? _selectionOperation;

        internal bool HasActivePreset => _presets.ActivePreset is not null;
        internal string? ActivePresetName => _presets.ActivePreset?.Name;

        internal CaptureSourceKind SelectedCaptureSource => Region is null
            ? Settings.CaptureSource
            : (_presets.ActivePreset ?? _manualPreset)?.Source ??
              (DesktopCapture.UsesSourceSelection && NativeCaptureSession.SourceIsWindow
                  ? CaptureSourceKind.Window
                  : CaptureSourceKind.Screen);

        internal string SelectedCaptureSourceKey => Region is null
            ? "SelectArea"
            : (_presets.ActivePreset ?? _manualPreset)?.Layout is not null
                ? "MainSourceLayout"
                : SelectedCaptureSource == CaptureSourceKind.Window
                    ? "MainSourceWindow"
                    : "MainSourceScreen";

        private void CancelSelection()
        {
            try
            {
                _selectionCancellation?.Cancel();
            }
            finally
            {
                try
                {
                    _selection?.Cancel();
                }
                finally
                {
                    _selectionDialog?.Close();
                }
            }
        }

        private Task BeginSelection(Func<CancellationToken, Task> action)
        {
            HostLogger.ZLogInformation(
                $"Region selection requested. Closing={IsClosing} Selecting={IsSelecting} Configuring={IsConfiguring}");
            if (!CanEditRecordingSettings || IsSelecting || IsConfiguring)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            IsSelecting = true;
            Notify();
            var cancellation = new CancellationTokenSource();
            _selectionCancellation = cancellation;
            _selectionOperation = RunSelectionAsync(action, cancellation);
            return _selectionOperation;
        }

        private async Task RunSelectionAsync(Func<CancellationToken, Task> action,
            CancellationTokenSource cancellation)
        {
            try
            {
                await action(cancellation.Token);
            }
            finally
            {
                _selection = null;
                _selectionDialog = null;
                _selectionCancellation = null;
                cancellation.Dispose();
                IsSelecting = false;
                HostLogger.ZLogInformation($"Region selection ended. Closing={IsClosing} Region={Region}");
                RefreshDisplaySelection();
                Notify();
            }
        }

        private Task SelectAsync()
        {
            return SelectSourceAsync(Settings.CaptureSource);
        }

        public async Task SelectSourceAsync(CaptureSourceKind source)
        {
            if (!CanEditRecordingSettings || IsSelecting || IsConfiguring) return;
            try
            {
                await BeginSelection(token => SelectCoreAsync(source, token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!IsClosing) SetStatus(ex.Message);
            }
        }

        private async Task SelectCoreAsync(CaptureSourceKind source, CancellationToken token)
        {
            var viewBeforeSelection = View;
            var wasVisible = viewBeforeSelection?.IsVisible == true;
            using var settingsVisibility = await CaptureWindowVisibilityLease.HideAsync(_settingsWindow,
                () => !IsClosing, token);
            var completed = false;
            try
            {
                await CaptureWindowVisibility.HideAsync(viewBeforeSelection, token);
                token.ThrowIfCancellationRequested();
                using var selection = new SelectionCoordinator(Screens, Settings.SnapRadius, Settings.EdgeThreshold,
                    source == CaptureSourceKind.Window, Settings.Cursor, Settings.Fps);
                ConfigureSelectionPreferences(selection);
                _selection = selection;
                var result = await selection.ShowAsync();
                token.ThrowIfCancellationRequested();
                if (result is not null)
                {
                    ValidateRegion(result.Region, !DesktopCapture.UsesSourceSelection && !result.WindowSource);
                    selection.CommitSource(token);
                    DeactivatePreset();
                    SetRegion(result.Region, !DesktopCapture.UsesSourceSelection && !result.WindowSource);
                    _manualPreset = result.CreatePreset();
                    _captureHistory.RecordRegion(result);
                    Settings.CaptureSource = result.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen;
                    completed = true;
                }
            }
            finally
            {
                if (!IsClosing && wasVisible && viewBeforeSelection is not null)
                {
                    var activate = viewBeforeSelection.ShowActivated;
                    viewBeforeSelection.ShowActivated = completed;
                    try
                    {
                        viewBeforeSelection.Show();
                    }
                    finally
                    {
                        viewBeforeSelection.ShowActivated = activate;
                    }
                }
            }
        }

        private Task SelectWindowAsync(string process, string title)
        {
            return BeginSelection(async token =>
            {
                var preset = WindowPreset.ForWindow(process, title) with
                {
                    Source = Settings.CaptureSource, Follow = true,
                };
                var windows = await Task.Run(WindowLocator.List, token);
                token.ThrowIfCancellationRequested();
                var target = WindowLocator.Find(preset, windows)
                             ?? throw new InvalidOperationException(UiText.Get("WindowNotFound"));
                ValidateRegion(target.Client, preset.Source == CaptureSourceKind.Screen);
                DeactivatePreset();
                SetRegion(target.Client, preset.Source == CaptureSourceKind.Screen);
                _manualPreset = preset;
            });
        }
    }
}
