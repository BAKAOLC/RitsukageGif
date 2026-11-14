using System.Diagnostics;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private static readonly TimeSpan MatchFailureWarningDelay = TimeSpan.FromSeconds(3);
        private readonly Action<ScrollCaptureWarningKind>? _warningSuppressed;
        private bool _fileOnlyWarningShown;
        private long _matchFailureStarted;
        private ScreenshotScrollingWarningDialog? _warningDialog;
        private bool _warningExcluded;
        private ScrollCapturePreferences _warningPreferences;
        private bool _warningQueued;
        private Task? _warningTask;

        private bool HasCaptureDialog => _clearDialog is not null || _warningDialog is not null;

        private bool ShowMatchFailureHint => _matchFailureStarted != 0 &&
                                             !_warningPreferences.SkipMatchFailureWarning &&
                                             Stopwatch.GetElapsedTime(_matchFailureStarted) >= MatchFailureWarningDelay;

        private void ResetCaptureWarnings()
        {
            _fileOnlyWarningShown = false;
            _matchFailureStarted = 0;
            _warningQueued = false;
        }

        private void ObserveCaptureWarnings(ScrollCaptureStatus status)
        {
            var unmatched = status is ScrollCaptureStatus.Unmatched or ScrollCaptureStatus.Ambiguous;
            if (!unmatched || _obstructed || _session.Paused || HasCaptureDialog) _matchFailureStarted = 0;
            else if (_matchFailureStarted == 0) _matchFailureStarted = Stopwatch.GetTimestamp();

            if (_warningQueued || HasCaptureDialog || _outputting || _closed || _outputToken.IsCancellationRequested ||
                _obstructed || _session.Paused || !_captureStarted || _dragStart is not null) return;

            if (_fileOnlyWarningShown || _session.OutputAxisLength <= FileOnlyWarningLength) return;
            const ScrollCaptureWarningKind kind = ScrollCaptureWarningKind.FileOnly;

            if (_warningPreferences.IsWarningSuppressed(kind))
            {
                _fileOnlyWarningShown = true;
                return;
            }

            _warningQueued = true;
            var generation = _session.Generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _session.Generation) return;
                _warningQueued = false;
                if (_closed || _outputToken.IsCancellationRequested || _outputting || HasCaptureDialog ||
                    _obstructed || _session.Paused || _dragStart is not null ||
                    _session.OutputAxisLength <= FileOnlyWarningLength)
                    return;

                _fileOnlyWarningShown = true;
                _warningTask = ShowCaptureWarningAsync(kind, generation);
            });
        }

        private async Task ShowCaptureWarningAsync(ScrollCaptureWarningKind kind, int generation)
        {
            try
            {
                await ShowOwnedCaptureWarningAsync(kind, generation);
            }
            finally
            {
                if (kind == ScrollCaptureWarningKind.FileOnly) _session.ResolveFileOnlyWarning(generation);
            }
        }

        private async Task ShowOwnedCaptureWarningAsync(ScrollCaptureWarningKind kind, int generation)
        {
            var dialog = new ScreenshotScrollingWarningDialog(kind);
            _warningDialog = dialog;
            _session.SetAutomaticHold(true);
            _cropPopup.IsOpen = false;
            RefreshShortcutNativeState();
            dialog.Opened += (_, _) =>
            {
                _warningExcluded = Exclude(dialog);
                UpdateObstruction();
            };
            dialog.PositionChanged += (_, _) => UpdateObstruction();
            dialog.SizeChanged += (_, _) => UpdateObstruction();
            try
            {
                var continueAutomatic = await dialog.ShowDialog<bool>(this);
                if (_closed || _outputToken.IsCancellationRequested || generation != _session.Generation) return;
                if (dialog.DoNotAskAgain)
                {
                    _warningPreferences = _warningPreferences.SuppressWarning(kind);
                    if (kind == ScrollCaptureWarningKind.FileOnly) _session.ConfigureFileOnlyWarning(0);
                    _warningSuppressed?.Invoke(kind);
                }

                if (kind == ScrollCaptureWarningKind.FileOnly && !continueAutomatic && _session.AutomaticEnabled)
                    _session.AutomaticEnabled = false;
            }
            catch (Exception error)
            {
                ScrollingUiLogger.ZLogError(error, $"Scrolling warning failed. Kind={kind.ToString()}");
                if (!_closed && generation == _session.Generation) _captureFailure = error.Message;
            }
            finally
            {
                _warningDialog = null;
                _warningExcluded = false;
                _session.SetAutomaticHold(false);
                if (!_closed)
                {
                    UpdateObstruction();
                    RefreshShortcutNativeState();
                }
            }
        }
    }
}
