using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Button ColorFormatsButton(List<ColorFormatDefinition> definitions)
        {
            var button = new Button { MinHeight = 28 };
            button.Bind(ContentProperty, LiveText.Resource("CaptureColorFormatsManage"));
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    var editor = new ColorFormatsWindow(definitions);
                    if (await editor.ShowDialog<List<ColorFormatDefinition>?>(this) is not { } result) return;
                    definitions.Clear();
                    definitions.AddRange(result);
                    RequestLiveApply();
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
            return button;
        }
    }
}
