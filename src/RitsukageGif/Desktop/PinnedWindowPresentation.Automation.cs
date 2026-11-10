namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal bool AutomationShadow => WindowShadow;

        internal void SetAutomationShadow(bool value)
        {
            SetWindowShadow(value);
        }
    }
}
