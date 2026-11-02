using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedWindowAttention : IDisposable
    {
        private static readonly IBrush[] Colors =
        [
            new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            new SolidColorBrush(Color.FromRgb(255, 201, 14)),
            new SolidColorBrush(Color.FromRgb(85, 213, 85)),
        ];
        private readonly Window _owner;

        private readonly Grid _content;
        private readonly Func<bool> _translucent;
        private Border? _border;
        private DispatcherTimer? _timer;
        private int _index = -1;
        private bool _disposed;

        internal PinnedWindowAttention(Window owner, Grid content, Func<bool> translucent)
        {
            _owner = owner;
            _content = content;
            _translucent = translucent;
            owner.Closed += Closed;
        }

        private bool IsFlashing => _index >= 0;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _owner.Closed -= Closed;
        }

        internal void Flash()
        {
            if (_disposed || !_owner.IsVisible || IsFlashing) return;
            if (_border is null)
            {
                _border = new() { IsVisible = false, IsHitTestVisible = false };
                _content.Children.Add(_border);
            }

            if (_timer is null)
            {
                _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
                _timer.Tick += Tick;
            }

            _index = 0;
            _owner.PropertyChanged += PropertyChanged;
            Paint();
            _timer.Start();
        }

        internal void Refresh()
        {
            if (IsFlashing) Paint();
        }

        private void Closed(object? sender, EventArgs args)
        {
            Dispose();
        }

        private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty && !_owner.IsVisible) Stop();
            else if (args.Property == Visual.OpacityProperty && IsFlashing) Paint();
        }

        private void Tick(object? sender, EventArgs args)
        {
            if (++_index >= Colors.Length || !_owner.IsVisible) Stop();
            else Paint();
        }

        private void Paint()
        {
            _border!.BorderBrush = Colors[_index];
            _border.BorderThickness = new(1);
            _border.IsVisible = true;
            _content.Opacity = _translucent() && _owner.Opacity > 0.6 ? 0.6 / _owner.Opacity : 1;
        }

        private void Stop()
        {
            if (!IsFlashing) return;
            _timer!.Stop();
            _index = -1;
            _owner.PropertyChanged -= PropertyChanged;
            _border!.IsVisible = false;
            _content.Opacity = 1;
        }
    }
}
