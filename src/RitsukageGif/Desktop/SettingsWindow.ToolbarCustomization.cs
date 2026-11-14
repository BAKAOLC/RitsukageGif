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
                    if (tools)
                    {
                        await new ToolbarToolsWindow(draft.Annotation.Toolbar, result => ApplyToolbar(result, true))
                            .ShowDialog(this);
                        return;
                    }

                    if (await new ToolbarActionsWindow(draft.Annotation.Toolbar)
                            .ShowDialog<ToolbarPreferences?>(this) is { } actions)
                        ApplyToolbar(actions, false);
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
            return button;

            void ApplyToolbar(ToolbarPreferences preferences, bool applyTools)
            {
                draft.Annotation = draft.Annotation with
                {
                    Toolbar = draft.Annotation.Toolbar.Merge(preferences, applyTools),
                };
                RequestLiveApply();
            }
        }
    }
}
