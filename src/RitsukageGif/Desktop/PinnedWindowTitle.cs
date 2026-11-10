using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedWindowTitle : Popup
    {
        private readonly TextBox _editor;
        private readonly Window _owner;
        private readonly Func<PinnedImagePreferences> _preferences;
        private bool _closed;
        private bool _editing;

        internal PinnedWindowTitle(Window owner, Control anchor, Func<PinnedImagePreferences> preferences)
        {
            _owner = owner;
            _preferences = preferences;
            PlacementTarget = anchor;
            Placement = PlacementMode.AnchorAndGravity;
            IsLightDismissEnabled = false;
            _editor = new()
            {
                MinWidth = 0, MinHeight = 26, Padding = new(8, 3), BorderThickness = new(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink, BorderBrush = AppTheme.Outline,
                FontSize = 13, IsReadOnly = true,
            };
            _editor.Bind(TextBox.PlaceholderTextProperty, LiveText.Resource("PinnedTitlePlaceholder"));
            Child = _editor;
            _editor.LostFocus += (_, _) => Commit();
            _editor.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter || !_editing)
                {
                    args.Handled = true;
                    return;
                }

                Commit();
                _owner.Activate();
                _owner.Focus();
                args.Handled = true;
            };
            _editor.PointerReleased += (_, _) =>
            {
                if (_editing) return;
                _owner.Activate();
                _owner.Focus();
            };
            _editor.TextChanged += (_, _) => Refresh();
            owner.Opened += (_, _) => Refresh();
            owner.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty || args.Property == Window.WindowStateProperty)
                    Refresh();
            };
            owner.Closing += (_, _) => Commit();
            owner.Closed += (_, _) =>
            {
                _closed = true;
                _editing = false;
                IsOpen = false;
            };
        }

        internal string Value { get; private set; } = "";

        internal event Action? Changed;

        internal void SetTitle(string value)
        {
            Value = value;
            _editor.Text = value;
            Refresh();
        }

        internal void BeginEdit()
        {
            if (_closed || !_owner.IsVisible) return;
            _editing = true;
            _editor.IsReadOnly = false;
            Refresh();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_editing || !_owner.IsVisible) return;
                _editor.Focus();
                _editor.SelectAll();
            }, DispatcherPriority.Input);
        }

        internal void Refresh()
        {
            if (_closed) return;
            if (!_owner.IsVisible || _owner.WindowState == WindowState.Minimized)
            {
                IsOpen = false;
                return;
            }

            _editor.Background = AppTheme.Surface;
            _editor.Foreground = AppTheme.Ink;
            _editor.BorderBrush = AppTheme.Outline;
            var top = _preferences().TitlePosition == PinnedTitlePosition.TopCenter;
            PlacementAnchor = top ? PopupAnchor.Top : PopupAnchor.Bottom;
            PlacementGravity = top ? PopupGravity.Top : PopupGravity.Bottom;
            var text = _editor.Text ?? "";
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight),
                _editor.FontSize, _editor.Foreground);
            _editor.Width = _editing ? Math.Max(160, formatted.Width + 24) : formatted.Width + 18;
            _editor.MaxWidth = Math.Max(160,
                _owner.Screens.ScreenFromWindow(_owner)?.WorkingArea.Width / _owner.RenderScaling - 24 ?? 600);
            IsOpen = _owner.IsVisible && _owner.WindowState != WindowState.Minimized &&
                     (_editing || Value.Length != 0);
        }

        private void Commit()
        {
            if (_closed || !_editing) return;
            _editing = false;
            _editor.IsReadOnly = true;
            var value = (_editor.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (Value != value)
            {
                Value = value;
                Changed?.Invoke();
            }

            Refresh();
        }
    }
}
