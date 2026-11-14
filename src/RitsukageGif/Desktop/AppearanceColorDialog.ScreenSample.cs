using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppearanceColorDialog
    {
        private static readonly ILogger SamplingLogger = LogHub.GetLogger("Desktop.ColorPicker");

        private Color? _colorBeforeSampling;
        private ToggleButton? _modernScreenPickButton;
        private Avalonia.PixelPoint? _requestedSamplePoint;
        private bool _sampleCompleted;
        private ScreenColorCompletion _sampleCompletion;
        private IPointer? _samplePointer;
        private long _sampleRevision;
        private Task _sampleShutdown = Task.CompletedTask;
        private DispatcherTimer? _sampleTracking;
        private ScreenColorSampler? _screenSampler;

        private static bool CanSampleScreen => PinnedGroupPointer.IsSupported;
        private bool IsSampling => _colorBeforeSampling.HasValue;

        private Button CreateScreenPickButton()
        {
            var button = _classic ? new Button() : _modernScreenPickButton = new();
            button.Name = "AppearancePickScreenColor";
            button.IsEnabled = CanSampleScreen;
            if (_classic)
            {
                button[!ContentProperty] = LiveText.Resource("AppearanceColorPickScreen");
            }
            else
            {
                button.Content = ToolbarIcon.Create(button, ToolbarIcon.FindDefinition("AppearanceColorEyedropper")!);
                ToolTip.SetTip(button, UiText.Get("AppearanceColorPickScreen"));
            }

            button.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible) BeginSampling();
            }, DispatcherPriority.Input);
            AddHandler(PointerMovedEvent, SamplePointerMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, SamplePointerPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, SamplePointerReleased, RoutingStrategies.Tunnel);
            PointerCaptureLost += (_, _) =>
            {
                if (IsSampling && _sampleCompletion == ScreenColorCompletion.Continue) CancelSampling();
            };
            return button;
        }

        private void BeginSampling()
        {
            if (!CanSampleScreen || IsSampling) return;
            _colorBeforeSampling = _color;
            if (_modernScreenPickButton is { } button) button.IsChecked = true;
            Cursor = new(StandardCursorType.Cross);
            _samplePointer?.Capture(this);
            RequestScreenSample(PinnedGroupPointer.Read());
            if (OperatingSystem.IsWindows())
            {
                _sampleTracking ??= new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
                _sampleTracking.Tick += SampleOutsideWindow;
                _sampleTracking.Start();
            }
        }

        private void SampleOutsideWindow(object? sender, EventArgs args)
        {
            if (_sampleCompletion == ScreenColorCompletion.Continue && PinnedGroupPointer.Read() is { } point &&
                point != _requestedSamplePoint)
                RequestScreenSample(point);
        }

        private void SamplePointerMoved(object? sender, PointerEventArgs args)
        {
            if (args.Pointer.Type != PointerType.Mouse) return;
            _samplePointer = args.Pointer;
            if (!IsSampling || _sampleCompletion != ScreenColorCompletion.Continue) return;
            if (!ReferenceEquals(args.Pointer.Captured, this)) args.Pointer.Capture(this);
            RequestScreenSample(SamplePosition(args));
        }

        private Avalonia.PixelPoint SamplePosition(PointerEventArgs args)
        {
            var local = args.GetPosition(this);
            return OperatingSystem.IsWindows()
                ? this.PointToScreen(default) + new Avalonia.PixelPoint((int)Math.Round(local.X * RenderScaling),
                    (int)Math.Round(local.Y * RenderScaling))
                : this.PointToScreen(local);
        }

        private void SamplePointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.Pointer.Type == PointerType.Mouse) _samplePointer = args.Pointer;
            if (!IsSampling) return;
            args.Handled = true;
        }

        private void SamplePointerReleased(object? sender, PointerReleasedEventArgs args)
        {
            if (!IsSampling || args.Pointer.Type != PointerType.Mouse) return;
            args.Handled = true;
            if (_sampleCompletion == ScreenColorCompletion.Continue)
                RequestScreenSample(SamplePosition(args), ScreenColorCompletion.FinishSampling, true);
        }

        private void RequestScreenSample(Avalonia.PixelPoint? samplePoint,
            ScreenColorCompletion completion = ScreenColorCompletion.Continue,
            bool refresh = false)
        {
            _sampleCompletion = completion;
            if (samplePoint is not { } location ||
                new Rect(ClientSize).Contains(this.PointToClient(location)) ||
                Screens.ScreenFromPoint(location) is null)
            {
                _requestedSamplePoint = null;
                ++_sampleRevision;
                CompleteScreenSample();
                return;
            }

            if (!refresh && _requestedSamplePoint == location)
            {
                if (_sampleCompleted) CompleteScreenSample();
                return;
            }

            _requestedSamplePoint = location;
            _sampleCompleted = false;
            _screenSampler ??= new([
                .. Screens.All.Select(screen => new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height)),
            ], _sampleShutdown, PublishSample, SamplingFailed);
            _screenSampler.Request(location, ++_sampleRevision);
        }

        private void PublishSample(ScreenColorSampler sampler, long revision, Color? color)
        {
            if (!ReferenceEquals(sampler, _screenSampler) || _sampleRevision != revision) return;
            if (color is { } sampled)
            {
                FromColor(Color.FromArgb(_color.A, sampled.R, sampled.G, sampled.B));
                _sampleCompleted = true;
            }
            else
            {
                _requestedSamplePoint = null;
            }

            CompleteScreenSample();
        }

        private void CompleteScreenSample()
        {
            var completion = _sampleCompletion;
            if (completion == ScreenColorCompletion.Continue) return;
            StopSampling();
            if (completion == ScreenColorCompletion.AcceptDialog) Close(_color);
        }

        private void SamplingFailed(ScreenColorSampler sampler, Exception error)
        {
            if (!ReferenceEquals(sampler, _screenSampler)) return;
            CancelSampling();
            SamplingLogger.ZLogWarning(error, $"Reading the screen color failed.");
        }

        private void CancelSampling()
        {
            var previous = _colorBeforeSampling;
            StopSampling();
            if (previous is { } color) FromColor(color);
        }

        private void StopSampling()
        {
            if (!IsSampling) return;
            _colorBeforeSampling = null;
            if (_sampleTracking is { } tracking)
            {
                tracking.Stop();
                tracking.Tick -= SampleOutsideWindow;
            }

            if (_modernScreenPickButton is { } button) button.IsChecked = false;
            _requestedSamplePoint = null;
            _sampleCompletion = ScreenColorCompletion.Continue;
            if (_screenSampler is { } sampler) _sampleShutdown = sampler.StopAsync();
            _screenSampler = null;
            if (_samplePointer is { Captured: { } captured } pointer && ReferenceEquals(captured, this))
                pointer.Capture(null);
            Cursor = null;
        }

        private void HandlePickerKey(object? sender, KeyEventArgs args)
        {
            if (IsSampling)
            {
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (args.Key)
                {
                    case Key.Escape:
                        CancelSampling();
                        break;
                    case Key.Enter:
                        if (_sampleCompletion == ScreenColorCompletion.Continue)
                            RequestScreenSample(PinnedGroupPointer.Read(),
                                _classic ? ScreenColorCompletion.FinishSampling : ScreenColorCompletion.AcceptDialog,
                                !_classic);
                        break;
                    case Key.Space when !_classic:
                        if (_sampleCompletion == ScreenColorCompletion.Continue)
                            RequestScreenSample(PinnedGroupPointer.Read(), ScreenColorCompletion.FinishSampling, true);
                        break;
                    default:
                        return;
                }

                args.Handled = true;
            }
            else if (!_classic && args is { Key: Key.Space, Source: not (TextBox or Button or ComboBox) } &&
                     CanSampleScreen)
            {
                args.Handled = true;
                BeginSampling();
            }
        }
    }
}
