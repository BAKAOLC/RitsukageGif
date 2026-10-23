using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Task? _selectionOperation;
        private CancellationTokenSource? _selectionCancellation;
        private Window? _selectionDialog;

        private void CancelSelection()
        {
            _selectionCancellation?.Cancel();
            _selection?.Cancel();
            _selectionDialog?.Close();
        }

        private Task BeginSelection(Func<CancellationToken, Task> action)
        {
            if (_recording || _busy || _selecting || _configuring || _closing)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            _selecting = true;
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
                _selecting = false;
                RefreshDisplaySelection();
                Notify();
            }
        }

        public Task SelectAsync() => SelectSourceAsync(_settings.CaptureSource);

        public async Task SelectSourceAsync(CaptureSourceKind source)
        {
            if (_recording || _busy || _selecting || _configuring || _closing) return;
            try
            {
                await BeginSelection(token => SelectCoreAsync(source, token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!_closing) SetStatus(ex.Message);
            }
        }

        private async Task SelectCoreAsync(CaptureSourceKind source, CancellationToken token)
        {
            var viewBeforeSelection = View;
            var wasVisible = viewBeforeSelection?.IsVisible == true;
            var completed = false;
            viewBeforeSelection?.Hide();
            try
            {
                if (wasVisible)
                    await Task.Delay(150, token); // Wait for the compositor to remove this window from the snapshot.
                token.ThrowIfCancellationRequested();
                using var selection = new SelectionCoordinator(Screens, _settings.SnapRadius, _settings.EdgeThreshold,
                    source == CaptureSourceKind.Window, _settings.Cursor, _settings.Fps);
                _selection = selection;
                var selected = await selection.ShowAsync();
                token.ThrowIfCancellationRequested();
                if (selected is { } result)
                {
                    ValidateRegion(result.Region, !DesktopCapture.UsesSourceSelection && !result.WindowSource);
                    selection.CommitSource(token);
                    DeactivatePreset();
                    SetRegion(result.Region, !DesktopCapture.UsesSourceSelection && !result.WindowSource);
                    _manualPreset = result.CreatePreset();
                    _settings.CaptureSource = result.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen;
                    completed = true;
                }
            }
            finally
            {
                if (!_closing && wasVisible && viewBeforeSelection is not null)
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
                    Source = _settings.CaptureSource, Follow = true
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
