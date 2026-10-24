using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    public sealed class App : Application
    {
        private ApplicationHost? _host;
        private FatalErrorWindow? _fatalError;

        public override void Initialize()
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
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

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            Trace.TraceError("Unhandled UI exception: {0}", args.Exception);
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            args.Handled = true;
            if (_fatalError is not null) return;
            _fatalError = new FatalErrorWindow(args.Exception);
            foreach (var window in desktop.Windows) window.IsEnabled = false;
            try
            {
                _host?.SuspendAfterFailure();
            }
            catch (Exception error)
            {
                Trace.TraceError("Cancelling after UI failure: {0}", error);
            }

            _fatalError.Closed += async (_, _) =>
            {
                try
                {
                    if (_host is not null) await _host.ShutdownAsync();
                }
                catch (Exception error)
                {
                    Trace.TraceError("Shutdown after UI failure: {0}", error);
                }
                finally
                {
                    desktop.Shutdown(1);
                }
            };
            _fatalError.Show();
        }
    }
}
