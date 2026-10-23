using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RitsukageGif.Desktop
{
    public sealed class App : Application
    {
        private ApplicationHost? _host;

        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Light;
            Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
            Styles.Add(new FlatStyles());
            Styles.Add(new EditorStyles());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                _host = new ApplicationHost(desktop);
                desktop.ShutdownRequested += async (_, e) =>
                {
                    if (_host.IsClosing) return;
                    e.Cancel = true;
                    await _host.ShutdownAsync();
                };
                if (!Program.StartHidden) _host.ShowMainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
