using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Control[] _defaultPresentationPages = [];

        private static Grid SettingRow<T>(string text, Control control, Func<AppSettings, T> getter,
            Func<T, T, bool>? equals = null)
        {
            var row = Labeled(text, control);
            return SettingsField.Track(row, row.Children[0], getter, equals);
        }

        private static TControl Setting<TControl, TValue>(TControl control, Func<AppSettings, TValue> getter,
            Func<TValue, TValue, bool>? equals = null) where TControl : Control
        {
            if (control.Parent is Grid { Children.Count: > 1 } row && row.Children[0] is TextBlock label)
                SettingsField.Track(row, label, getter, equals);
            else SettingsField.Track(control, control, getter, equals);
            return control;
        }

        private void RefreshDefaultPresentation()
        {
            if (_draft is null) return;
            foreach (var page in _defaultPresentationPages)
            {
                SettingsField.Refresh(page, _draft);
                foreach (var control in page.GetLogicalDescendants().OfType<Control>())
                    SettingsField.Refresh(control, _draft);
            }
        }
    }
}
