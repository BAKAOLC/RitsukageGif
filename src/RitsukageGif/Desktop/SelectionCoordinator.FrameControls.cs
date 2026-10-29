using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private uint _frameBorderColor = 0xFF000000;
        private int _frameBorderStrength = 2;
        private TaskCompletionSource<bool>? _frameRefreshRelease;
        private uint _frameShadowColor = 0x80000000;
        private int _frameShadowStrength = 12;

        internal bool FrameRoundEnabled { get; private set; }

        internal double FrameCornerRadius { get; private set; } = 12;

        internal bool FrameRoundInitialEnabled
        {
            set => FrameRoundEnabled = value;
        }

        internal double FrameRoundInitialRadius
        {
            set => FrameCornerRadius = double.IsFinite(value) ? Math.Clamp(value, 0, 60) : 12;
        }

        internal bool FrameDecorationEnabled { get; private set; }

        internal CaptureDecorationKind FrameDecorationKind { get; private set; } = CaptureDecorationKind.Shadow;

        internal int FrameDecorationStrength => FrameDecorationKind == CaptureDecorationKind.Shadow
            ? _frameShadowStrength
            : _frameBorderStrength;

        private uint FrameDecorationColor => FrameDecorationKind == CaptureDecorationKind.Shadow
            ? _frameShadowColor
            : _frameBorderColor;

        internal bool KeepDecoration { get; set; }

        internal bool KeepDecorationInitial
        {
            set => KeepDecoration = value;
        }

        internal CaptureDecoration RememberedDecoration => Decoration with { CornerRadius = 0 };
        internal bool CanRefreshFrame => ScreenshotMode && !UsesLocalSource;
        private bool FrameRefreshHeld => _frameRefreshRelease is not null;

        internal Func<Action, Action, Action<Exception>, CancellationToken, Task<IDisposable>>?
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

        internal bool CanShowFrameControls => ScreenshotMode && Snapshot.Phase == SelectionPhase.Selected &&
                                              Annotations is { IsBusy: false, IsDrawing: false, IsEditingText: false };

        internal CaptureDecoration Decoration
        {
            get
            {
                var radius = FrameRoundEnabled ? FrameCornerRadius : 0;
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

        internal CaptureDecoration InitialDecoration
        {
            set => RestoreFrameDecoration(value);
        }

        private void RestoreFrameDecoration(CaptureDecoration? decoration)
        {
            var value = decoration ?? CaptureDecoration.None;
            FrameRoundEnabled = double.IsFinite(value.CornerRadius) && value.CornerRadius > 0;
            if (FrameRoundEnabled) FrameCornerRadius = Math.Clamp(value.CornerRadius, 0, 60);
            FrameDecorationEnabled = value.Kind is CaptureDecorationKind.Shadow or CaptureDecorationKind.Border;
            if (FrameDecorationEnabled)
            {
                FrameDecorationKind = value.Kind;
                if (value.Kind == CaptureDecorationKind.Shadow)
                {
                    _frameShadowStrength = Math.Clamp(value.Strength, 1, 50);
                    _frameShadowColor = value.Color;
                }
                else
                {
                    _frameBorderStrength = Math.Clamp(value.Strength, 1, 50);
                    _frameBorderColor = value.Color;
                }
            }

            Refresh();
        }

        internal void SetFrameRound(bool enabled)
        {
            FrameRoundEnabled = enabled;
            Refresh();
        }

        internal void SetFrameRadius(double radius)
        {
            if (!double.IsFinite(radius)) return;
            FrameCornerRadius = Math.Clamp(Math.Round(radius), 0, 60);
            FrameRoundEnabled = FrameCornerRadius > 0;
            Refresh();
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
        }

        internal void SetFrameDecorationStrength(int strength)
        {
            strength = Math.Clamp(strength, 1, 50);
            if (FrameDecorationKind == CaptureDecorationKind.Shadow) _frameShadowStrength = strength;
            else _frameBorderStrength = strength;
            FrameDecorationEnabled = true;
            Refresh();
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
        }

        internal void StartFrameRefresh()
        {
            if (!CanRefreshFrame || _finished || _screenshotCompleting || _state.Dragging ||
                Annotations is not { IsBusy: false, IsDrawing: false }) return;
            _historyWork = _screenshotTask = RefreshFrozenFramesAsync(ResolveFrameRefreshObserver());
        }

        private Func<Action, Action, Action<Exception>, CancellationToken, Task<IDisposable>>?
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
