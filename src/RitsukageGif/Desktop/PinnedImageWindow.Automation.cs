namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        internal double AutomationScrollX => _viewport.Offset.X * RenderScaling;
        internal double AutomationScrollY => _viewport.Offset.Y * RenderScaling;

        internal PinTransparentLayer? AutomationTransparentLayer => CanChooseTransparentLayer
            ? (_transparentLayer ?? CurrentPreferences.TransparentLayer) switch
            {
                PinnedTransparentLayerMode.IgnoreMouse => PinTransparentLayer.IgnoreMouse,
                PinnedTransparentLayerMode.AcceptingMouse => PinTransparentLayer.AcceptingMouse,
                PinnedTransparentLayerMode.Checkerboard => PinTransparentLayer.Checkerboard,
                _ => throw new ArgumentOutOfRangeException(nameof(AutomationTransparentLayer)),
            }
            : null;

        internal bool AutomationCanIgnoreTransparentInput => CanIgnoreTransparentInput;

        internal double AutomationZoom => CurrentPinZoom;
        internal bool AutomationCanResize => !_busy && !IsInteractionLocked && _annotation is null;

        // ReSharper disable once ConvertToAutoProperty
        internal bool AutomationThumbnail => _isThumbnail;
        internal bool AutomationAnnotationsVisible => !IsMarkHidden;
        internal bool AutomationCanHideAnnotations => !IsMarkEditing;

        internal void SetAutomationScroll(double? x, double? y)
        {
            if (!AutomationCanResize)
                throw new InvalidOperationException("The pin cannot scroll while locked, busy or editing.");
            _viewport.Offset = new(x is { } left ? left / RenderScaling : _viewport.Offset.X,
                y is { } top ? top / RenderScaling : _viewport.Offset.Y);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetAutomationTransparentLayer(PinTransparentLayer mode)
        {
            if (!CanChooseTransparentLayer || !Enum.IsDefined(mode))
                throw new NotSupportedException("The pin does not support the requested transparent layer mode.");
            if (mode == PinTransparentLayer.IgnoreMouse && !CanIgnoreTransparentInput)
                throw new NotSupportedException("Transparent input exclusion is not supported for this pin.");
            _transparentLayer = mode switch
            {
                PinTransparentLayer.IgnoreMouse => PinnedTransparentLayerMode.IgnoreMouse,
                PinTransparentLayer.AcceptingMouse => PinnedTransparentLayerMode.AcceptingMouse,
                PinTransparentLayer.Checkerboard => PinnedTransparentLayerMode.Checkerboard,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            RefreshTransparentBackground();
            RefreshHdrPreview();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetAutomationThumbnailRegion(PixelRect region)
        {
            if (!AutomationCanResize || LiveCaptureActive)
                throw new InvalidOperationException("The pin cannot select a thumbnail region in its current state.");
            if (region.IsEmpty ||
                region.Intersect(new(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height)).IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(region));
            SetThumbnailRegion(region);
        }

        internal async Task TransformAutomationAsync(PinTransform transform, CancellationToken token)
        {
            var operation = transform switch
            {
                PinTransform.RotateClockwise => PinnedImageTransform.RotateClockwise,
                PinTransform.RotateCounterclockwise => PinnedImageTransform.RotateCounterclockwise,
                PinTransform.FlipHorizontal => PinnedImageTransform.FlipHorizontal,
                PinTransform.FlipVertical => PinnedImageTransform.FlipVertical,
                PinTransform.Grayscale => PinnedImageTransform.Grayscale,
                PinTransform.Invert => PinnedImageTransform.Invert,
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
            if (!CanTransformImage(operation))
                throw new InvalidOperationException("The pin cannot be transformed in its current state.");
            await RunAutomationAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
                await ApplyImageTransformAsync(operation, linked.Token);
            });
        }

        internal Task AdjustAutomationBrightnessAsync(double delta, CancellationToken token)
        {
            if (!double.IsFinite(delta) || Math.Abs(delta) > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (_closed || _busy || _annotation is not null || IsMouseThrough)
                throw new InvalidOperationException("The pin cannot be processed in its current state.");
            return RunAutomationAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
                await ApplyImageProcessingAsync(_colorMatrixState.AdjustBrightness((float)delta), linked.Token);
            });
        }

        internal async Task ResetAutomationProcessingAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_closed || _busy || _annotation is not null || IsMouseThrough || IsInteractionLocked)
                throw new InvalidOperationException("The pin cannot be reset in its current state.");
            await RunAutomationAsync(ResetImageProcessingAsync);
        }

        internal void SetAutomationAnnotationsVisible(bool value)
        {
            if (IsMarkEditing) throw new InvalidOperationException("Finish interactive annotation editing first.");
            SetMarkHidden(!value);
        }

        internal void SetAutomationTopmost(bool value)
        {
            SetTopmost(value);
        }

        internal void SetAutomationZoom(double value)
        {
            if (!AutomationCanResize)
                throw new InvalidOperationException("The pin cannot be resized while locked or editing.");
            ResizeAt(new(PinnedWindowPresentation.ShadeInset, PinnedWindowPresentation.ShadeInset), value);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void FitAutomation(int width, int height)
        {
            var display = DisplayRegion;
            var size = _viewTransform.ViewSize(new(display.Width, display.Height));
            var inset = PinnedWindowPresentation.ShadeInset * 2 * RenderScaling;
            var availableWidth = Math.Max(1, width - inset);
            var availableHeight = Math.Max(1, height - inset - (_hdrSource is null ? 0 : 20 * RenderScaling));
            SetAutomationZoom(Math.Min(availableWidth / size.Width, availableHeight / size.Height));
        }
    }
}
