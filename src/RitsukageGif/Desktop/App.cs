using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Semi.Avalonia;

namespace RitsukageGif.Desktop
{
    public sealed class App : Application
    {
        private static readonly ILogger AppLogger = LogHub.GetLogger("Desktop.Application");
        private FatalErrorWindow? _fatalError;
        private ApplicationHost? _host;

        public override void Initialize()
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            RequestedThemeVariant = ThemeVariant.Light;
            Styles.Add(new SemiTheme
            {
                Locale = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
                    ? "zh-CN"
                    : "en-US"),
            });
            Styles.Add(new AppTheme());
            Styles.Add(new EditorStyles());
            LiveText.RefreshResources();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _host = new(desktop);
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
            AppLogger.ZLogCritical(args.Exception, $"Unhandled UI exception.");
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            args.Handled = true;
            if (_fatalError is not null) return;
            _fatalError = new(args.Exception);
            foreach (var window in desktop.Windows.ToArray())
            {
                WindowDiagnostics.Write(AppLogger, "BeforeFatalCancellation", window);
                window.IsEnabled = false;
            }

            try
            {
                _host?.SuspendAfterFailure();
                foreach (var window in desktop.Windows.ToArray())
                    WindowDiagnostics.Write(AppLogger, "AfterFatalCancellation", window);
            }
            catch (Exception error)
            {
                AppLogger.ZLogError(error, $"Failure suspension could not complete.");
            }

            _fatalError.Closed += async (_, _) =>
            {
                try
                {
                    if (_host is not null) await _host.ShutdownAsync();
                }
                catch (Exception error)
                {
                    AppLogger.ZLogError(error, $"Shutdown after UI failure failed.");
                }
                finally
                {
                    desktop.Shutdown(1);
                }
            };
            _fatalError.Show();
            _fatalError.Activate();
            WindowDiagnostics.Write(AppLogger, "FatalErrorShown", _fatalError);
        }
    }
}
