using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Dialogs;
using Avalonia.Input;
using Avalonia.Native.Interop;
using Avalonia.Native.Interop.Impl;
using Avalonia.Reactive;
using Avalonia.Threading;

namespace Avalonia.Native
{
    internal class AvaloniaNativeMenuExporter : ITopLevelNativeMenuExporter
    {

        private readonly IAvaloniaNativeFactory _factory;
        private readonly MenuTarget _target;
        private bool _resetQueued = true;
        private readonly IAvnWindow? _nativeWindow;
        private NativeMenu? _menu;
        private __MicroComIAvnMenuProxy? _nativeMenu;
        private readonly IAvnTrayIcon? _trayIcon;
        private readonly IAvnApplicationCommands? _applicationCommands;

        public AvaloniaNativeMenuExporter(IAvnWindow nativeWindow, IAvaloniaNativeFactory factory)
        {
            _factory = factory;
            _target = MenuTarget.Window;
            _nativeWindow = nativeWindow;
            _applicationCommands = _factory.CreateApplicationCommands();

            DoLayoutReset();
        }

        public AvaloniaNativeMenuExporter(IAvaloniaNativeFactory factory)
        {
            _factory = factory;
            _target = MenuTarget.Application;
            _applicationCommands = _factory.CreateApplicationCommands();

            DoLayoutReset();
        }

        public AvaloniaNativeMenuExporter(IAvnTrayIcon trayIcon, IAvaloniaNativeFactory factory)
        {
            _factory = factory;
            _target = MenuTarget.TrayIcon;
            _trayIcon = trayIcon;
            _applicationCommands = _factory.CreateApplicationCommands();

            DoLayoutReset();
        }

        internal AvaloniaNativeMenuExporter(IAvaloniaNativeFactory factory, MenuTarget target)
        {
            _factory = factory;
            _target = target;
            _resetQueued = false;

            var macOpts = AvaloniaLocator.Current.GetService<MacOSPlatformOptions>() ?? new MacOSPlatformOptions();

            if (macOpts.DisableNativeMenus) return;

            NativeDock.MenuProperty.Changed.Subscribe(args =>
            {
                if (args.Sender is Application) SetNativeMenu(args.NewValue.GetValueOrDefault());
            });

            var app = Application.Current;
            if (app is not null)
            {
                var dockMenu = NativeDock.GetMenu(app);
                if (dockMenu is not null) SetNativeMenu(dockMenu);
            }
        }

        public bool IsNativeMenuExported { get; private set; }

        public event EventHandler OnIsNativeMenuExportedChanged
        {
            add { }
            remove { }
        }

        public void SetNativeMenu(NativeMenu? menu)
        {
            if (_target == MenuTarget.Dock)
            {
                _menu = menu;

                if (_menu is not null) DoLayoutReset(true);
            }
            else
            {
                _menu = menu ?? [];
                DoLayoutReset(true);
            }
        }

        internal void UpdateIfNeeded()
        {
            if (_resetQueued) DoLayoutReset();
        }

        private static NativeMenu CreateDefaultAppMenu()
        {
            var result = new NativeMenu();

            var aboutItem = new NativeMenuItem("About Avalonia");

            aboutItem.Click += async (_, _) =>
            {
                var dialog = new AboutAvaloniaDialog();

                if (Application.Current is
                    {
                        ApplicationLifetime: IClassicDesktopStyleApplicationLifetime
                        {
                            MainWindow: { IsVisible: true } mainWindow,
                        },
                    })
                    await dialog.ShowDialog(mainWindow);
                else
                    dialog.Show();
            };

            result.Add(aboutItem);

            return result;
        }

        private void PopulateStandardOsxMenuItems(NativeMenu appMenu)
        {
            appMenu.Add(new NativeMenuItemSeparator());

            var servicesMenu = new NativeMenuItem("Services")
            {
                Menu = new() { [MacOsNativeMenuCommands.IsServicesSubmenuProperty] = true },
            };

            appMenu.Add(servicesMenu);

            appMenu.Add(new NativeMenuItemSeparator());

            var hideItem = new NativeMenuItem("Hide " + (Application.Current?.Name ?? "Application"))
            {
                Gesture = new(Key.H, KeyModifiers.Meta),
            };

            hideItem.Click += (_, _) => { _applicationCommands?.HideApp(); };

            appMenu.Add(hideItem);

            var hideOthersItem = new NativeMenuItem("Hide Others")
            {
                Gesture = new(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt),
            };
            hideOthersItem.Click += (_, _) => { _applicationCommands?.HideOthers(); };
            appMenu.Add(hideOthersItem);

            var showAllItem = new NativeMenuItem("Show All");
            showAllItem.Click += (_, _) => { _applicationCommands?.ShowAll(); };

            appMenu.Add(showAllItem);

            appMenu.Add(new NativeMenuItemSeparator());

            var quitItem = new NativeMenuItem("Quit") { Gesture = new(Key.Q, KeyModifiers.Meta) };
            quitItem.Click += (_, _) =>
            {
                switch (Application.Current)
                {
                    case { ApplicationLifetime: IClassicDesktopStyleApplicationLifetime lifetime }:
                        lifetime.TryShutdown();
                        break;
                    case { ApplicationLifetime: IControlledApplicationLifetime controlledLifetime }:
                        controlledLifetime.Shutdown();
                        break;
                }
            };

            appMenu.Add(quitItem);
        }

        private void DoLayoutReset()
        {
            DoLayoutReset(false);
        }

        private void DoLayoutReset(bool forceUpdate)
        {
            var macOpts = AvaloniaLocator.Current.GetService<MacOSPlatformOptions>() ?? new MacOSPlatformOptions();

            if (macOpts.DisableNativeMenus) return;

            if (_resetQueued || forceUpdate)
            {
                _resetQueued = false;

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (_target)
                {
                    case MenuTarget.Application:
                    {
                        var app = Application.Current;
                        var appMenu = app is null ? null : NativeMenu.GetMenu(app);

                        if (appMenu == null)
                        {
                            appMenu = CreateDefaultAppMenu();

                            if (app is not null)
                                NativeMenu.SetMenu(app, appMenu);
                        }

                        SetMenu(appMenu);
                        break;
                    }

                    case MenuTarget.Window:
                    {
                        if (_menu != null) SetMenu(_nativeWindow, _menu);
                        break;
                    }

                    case MenuTarget.TrayIcon:
                    {
                        if (_menu != null) SetMenu(_trayIcon, _menu);
                        break;
                    }

                    case MenuTarget.Dock:
                    {
                        if (_menu != null) SetDockMenu(_menu);
                        break;
                    }
                }

                IsNativeMenuExported = true;
            }
        }

        internal void QueueReset()
        {
            if (_resetQueued)
                return;
            _resetQueued = true;
            Dispatcher.UIThread.Post(DoLayoutReset, DispatcherPriority.Background);
        }

        private void SetMenu(NativeMenu menu)
        {
            var menuItem = menu.Parent;

            var appMenuHolder = menuItem?.Parent;

            menuItem ??= new();

            appMenuHolder ??= [menuItem];

            menuItem.Menu = menu;

            var setMenu = false;

            if (_nativeMenu is null)
            {
                _nativeMenu = __MicroComIAvnMenuProxy.Create(_factory);

                _nativeMenu.Initialize(this, appMenuHolder, "");

                var macOpts = AvaloniaLocator.Current.GetService<MacOSPlatformOptions>();

                if (macOpts is not { DisableDefaultApplicationMenuItems: true }) PopulateStandardOsxMenuItems(menu);

                setMenu = true;
            }

            _nativeMenu.Update(_factory, appMenuHolder);

            if (setMenu) _factory.SetAppMenu(_nativeMenu);
        }

        private void SetMenu(IAvnWindow? avnWindow, NativeMenu menu)
        {
            var setMenu = false;

            if (_nativeMenu is null)
            {
                _nativeMenu = __MicroComIAvnMenuProxy.Create(_factory);

                _nativeMenu.Initialize(this, menu, "");

                setMenu = true;
            }

            _nativeMenu.Update(_factory, menu);

            if (setMenu) avnWindow?.SetMainMenu(_nativeMenu);
        }

        private void SetMenu(IAvnTrayIcon? trayIcon, NativeMenu menu)
        {
            var setMenu = false;

            if (_nativeMenu is null)
            {
                _nativeMenu = __MicroComIAvnMenuProxy.Create(_factory);

                _nativeMenu.Initialize(this, menu, "");

                setMenu = true;
            }

            _nativeMenu.Update(_factory, menu);

            if (setMenu) trayIcon?.SetMenu(_nativeMenu);
        }

        private void SetDockMenu(NativeMenu menu)
        {
            var setMenu = false;

            if (_nativeMenu is null)
            {
                _nativeMenu = __MicroComIAvnMenuProxy.Create(_factory);

                _nativeMenu.Initialize(this, menu, "");

                setMenu = true;
            }

            _nativeMenu.Update(_factory, menu);

            if (setMenu) _factory.SetDockMenu(_nativeMenu);
        }

        internal enum MenuTarget
        {
            Application,
            Window,
            TrayIcon,
            Dock,
        }
    }
}
