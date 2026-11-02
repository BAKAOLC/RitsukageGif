using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private TextBlock? _startupStatus;
        private Func<bool>? _startupRequested;
        internal Func<StartupRegistrationStatus>? StartupStatus { private get; init; }

        private TextBlock StartupStatusMessage(Func<bool> requested)
        {
            _startupRequested = requested;
            var message = new TextBlock
            {
                Foreground = AppTheme.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, IsVisible = false,
            };
            _startupStatus = message;
            message.AttachedToVisualTree += (_, _) => RefreshStartupStatus();
            Activated += ActivatedStartup;
            RegisterContentCleanup(() => Activated -= ActivatedStartup);
            SubscribeContentLanguage(() => RefreshStartupStatus());
            return message;

            void ActivatedStartup(object? sender, EventArgs args)
            {
                RefreshStartupStatus();
            }
        }

        internal void RefreshStartupStatus(StartupRegistrationStatus? knownStatus = null)
        {
            if (!IsVisible || _startupStatus is not { } message || StartupStatus is null) return;
            try
            {
                var status = knownStatus ?? StartupStatus();
                var key = status switch
                {
                    StartupRegistrationStatus.ApprovalRequired => "SystemStartupApprovalRequired",
                    StartupRegistrationStatus.Disabled when _startupRequested?.Invoke() == true =>
                        "SystemStartupDisabled",
                    StartupRegistrationStatus.Enabled when _startupRequested?.Invoke() == false =>
                        "SystemStartupEnabled",
                    _ => null,
                };
                message.Foreground = AppTheme.Muted;
                message.IsVisible = key is not null;
                if (key is not null) message.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            }
            catch (Exception error)
            {
                message.Text = error.Message;
                message.Foreground = AppTheme.Error;
                message.IsVisible = true;
            }
        }
    }
}
