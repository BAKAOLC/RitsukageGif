using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private uint _frameBorderColor = 0xFFFFFFFF;
        private int _frameBorderStrength = 8;
        private TaskCompletionSource<bool>? _frameRefreshRelease;
        private uint _frameShadowColor = 0xFFA0A0A4;
        private int _frameShadowStrength = 8;

        internal bool FrameRoundEnabled { get; private set; }

        internal double FrameCornerRadius { get; private set; }

        internal bool FrameRoundInitialEnabled
        {
            set => FrameRoundEnabled = value;
        }

        internal double FrameRoundInitialRadius
        {
            set => FrameCornerRadius = double.IsFinite(value) ? Math.Clamp(value, 0, 60) : 0;
        }

        internal bool FrameDecorationEnabled { get; private set; }

        internal CaptureDecorationKind FrameDecorationKind { get; private set; } = CaptureDecorationKind.Shadow;

        internal int FrameDecorationStrength => FrameDecorationKind == CaptureDecorationKind.Shadow
            ? _frameShadowStrength
            : _frameBorderStrength;

        private uint FrameDecorationColor => FrameDecorationKind == CaptureDecorationKind.Shadow
            ? _frameShadowColor
            : _frameBorderColor;

        internal bool KeepDecoration
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                FramePreferencesChanged?.Invoke();
            }
        }

        internal bool KeepDecorationInitial
        {
            set => KeepDecoration = value;
        }

        internal CaptureFrameDecorationPreferences FrameDecorationPreferences => new()
        {
            Kind = FrameDecorationKind, ShadowStrength = _frameShadowStrength, ShadowColor = _frameShadowColor,
            BorderStrength = _frameBorderStrength, BorderColor = _frameBorderColor,
        };

        internal bool CanRefreshFrame => ScreenshotMode && !ScreenshotPreview;
        internal bool RectangularSelection => ShapeMode == SelectionShapeMode.Rectangle && _selectionShape is null;
        private bool FrameRefreshHeld => _frameRefreshRelease is not null;

        internal Func<Action, Action, Action<Exception>, CancellationToken, Task<IAsyncDisposable>>?
            FrameRefreshObserverFactory
        {
            get;
            // ReSharper disable once UnusedMember.Global
            set
            {
                Dispatcher.UIThread.VerifyAccess();
                if (_screenshotCompleting) throw new InvalidOperationException();
                field = value;
            }
        }

        internal bool CanShowFrameControls => ScreenshotMode && CapturePreferences.ShowSecondaryButtons &&
                                              Snapshot.Phase == SelectionPhase.Selected &&
                                              Annotations is { IsBusy: false, IsDrawing: false, IsEditingText: false };

        internal CaptureDecoration Decoration
        {
            get
            {
                var radius = FrameRoundEnabled && RectangularSelection ? FrameCornerRadius : 0;
                var kind = FrameDecorationEnabled ? FrameDecorationKind : CaptureDecorationKind.None;
                var strength = FrameDecorationEnabled ? FrameDecorationStrength : 0;
                var color = FrameDecorationColor;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (field.CornerRadius != radius || field.Kind != kind ||
                    field.Strength != strength || field.Color != color)
                    field = new(radius, kind, strength, color);
                return field;
            }
        } = CaptureDecoration.None;

        internal CaptureFrameDecorationPreferences InitialDecoration
        {
            set => RestoreFrameDecorationPreferences(value);
        }

        internal event Action? FramePreferencesChanged;

        private void RestoreFrameDecorationPreferences(CaptureFrameDecorationPreferences decoration)
        {
            FrameDecorationEnabled = KeepDecoration;
            FrameDecorationKind = decoration.Kind;
            _frameShadowStrength = decoration.ShadowStrength;
            _frameShadowColor = decoration.ShadowColor;
            _frameBorderStrength = decoration.BorderStrength;
            _frameBorderColor = decoration.BorderColor;
            Refresh();
        }

        private void RestoreFrameDecoration(CaptureDecoration decoration)
        {
            FrameRoundEnabled = decoration.CornerRadius > 0;
            if (FrameRoundEnabled) FrameCornerRadius = Math.Clamp(decoration.CornerRadius, 0, 60);
            FrameDecorationEnabled = decoration.Kind is CaptureDecorationKind.Shadow or CaptureDecorationKind.Border;
            if (FrameDecorationEnabled)
            {
                FrameDecorationKind = decoration.Kind;
                if (decoration.Kind == CaptureDecorationKind.Shadow)
                {
                    _frameShadowStrength = Math.Clamp(decoration.Strength, 1, 50);
                    _frameShadowColor = decoration.Color;
                }
                else
                {
                    _frameBorderStrength = Math.Clamp(decoration.Strength, 1, 50);
                    _frameBorderColor = decoration.Color;
                }
            }

            Refresh();
        }

        internal void SetFrameRound(bool enabled)
        {
            FrameRoundEnabled = enabled;
            Refresh();
            FramePreferencesChanged?.Invoke();
        }

        internal void SetFrameRadius(double radius)
        {
            if (!double.IsFinite(radius)) return;
            FrameCornerRadius = Math.Clamp(Math.Round(radius), 0, 60);
            if (FrameCornerRadius > 0) FrameRoundEnabled = true;
            Refresh();
            FramePreferencesChanged?.Invoke();
        }

        internal void SetFrameDecoration(bool enabled)
        {
            FrameDecorationEnabled = enabled;
            Refresh();
        }

        internal void SetFrameDecorationKind(CaptureDecorationKind kind)
        {
            if (kind is not (CaptureDecorationKind.Shadow or CaptureDecorationKind.Border)) return;
            FrameDecorationKind = kind;
            FrameDecorationEnabled = true;
            Refresh();
            FramePreferencesChanged?.Invoke();
        }

        internal void SetFrameDecorationStrength(int strength)
        {
            strength = Math.Clamp(strength, 1, 50);
            if (FrameDecorationKind == CaptureDecorationKind.Shadow) _frameShadowStrength = strength;
            else _frameBorderStrength = strength;
            FrameDecorationEnabled = true;
            Refresh();
            FramePreferencesChanged?.Invoke();
        }

        internal uint GetFrameDecorationColor(CaptureDecorationKind kind)
        {
            return kind == CaptureDecorationKind.Shadow ? _frameShadowColor : _frameBorderColor;
        }

        internal void SetFrameDecorationColor(CaptureDecorationKind kind, uint color)
        {
            FrameDecorationKind = kind;
            if (FrameDecorationKind == CaptureDecorationKind.Shadow) _frameShadowColor = color;
            else _frameBorderColor = color;
            FrameDecorationEnabled = true;
            Refresh();
            FramePreferencesChanged?.Invoke();
        }

        internal void StartFrameRefresh()
        {
            if (!CanRefreshFrame || _finished || _screenshotCompleting || _state.Dragging ||
                Annotations is not { IsBusy: false, IsDrawing: false }) return;
            _historyWork = _screenshotTask = RefreshFrozenFramesAsync(ResolveFrameRefreshObserver());
        }

        private Func<Action, Action, Action<Exception>, CancellationToken, Task<IAsyncDisposable>>?
            ResolveFrameRefreshObserver()
        {
            if (FrameRefreshObserverFactory is { } observer) return observer;
            return ScopedCaptureInputObserver.IsSupported ? ScopedCaptureInputObserver.StartAsync : null;
        }

        internal void StopFrameRefresh()
        {
            _frameRefreshRelease?.TrySetResult(true);
        }
    }
}
