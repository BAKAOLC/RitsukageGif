using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Button ToolbarCustomizationButton(AppSettings draft, bool tools)
        {
            var button = new Button
            {
                Name = tools ? "ToolbarToolsCustomization" : "ToolbarActionsCustomization", MinHeight = 28,
            };
            button.Bind(ContentProperty, LiveText.Resource("ToolbarEditButtons"));
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    Window editor = tools
                        ? new ToolbarToolsWindow(draft.Annotation.Toolbar)
                        : new ToolbarActionsWindow(draft.Annotation.Toolbar);
                    if (await editor.ShowDialog<ToolbarPreferences?>(this) is { } result)
                    {
                        draft.Annotation = draft.Annotation with
                        {
                            Toolbar = draft.Annotation.Toolbar.Merge(result, tools),
                        };
                        RequestLiveApply();
                    }
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
