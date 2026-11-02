using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Native.Interop;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using MicroCom.Runtime;

namespace Avalonia.Native
{
    internal sealed partial class AvaloniaNativePlatform : IWindowingPlatform, IDisposable
    {
        private readonly IAvaloniaNativeFactory _factory;
        private AvaloniaNativePlatformOptions? _options;
        private IPlatformGraphics? _platformGraphics;
        private int _isDisposed;
        private static readonly KeyboardDevice SKeyboardDevice = new();

        private AvaloniaNativePlatform(IAvaloniaNativeFactory factory)
        {
            _factory = factory;
        }

        internal static Compositor Compositor { get; private set; } = null!;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
                return;

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _factory.Dispose();
        }

        public ITrayIconImpl CreateTrayIcon()
        {
            return new TrayIconImpl(_factory);
        }

        public IWindowImpl CreateWindow()
        {
            return new WindowImpl(_factory, _options ?? new AvaloniaNativePlatformOptions());
        }

        public IWindowImpl CreateEmbeddableWindow()
        {
            throw new NotImplementedException();
        }

        public ITopLevelImpl CreateEmbeddableTopLevel()
        {
            return new EmbeddableTopLevelImpl(_factory);
        }

        public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> zOrder)
        {
            for (var i = 0; i < windows.Length; i++) zOrder[i] = (windows[i] as WindowImpl)?.ZOrder?.ToInt64() ?? 0;
        }

        [LibraryImport("libAvaloniaNative")]
        private static partial IntPtr CreateAvaloniaNative();

        private static AvaloniaNativePlatform Initialize(IntPtr factory, AvaloniaNativePlatformOptions options)
        {
            var factoryProxy = MicroComRuntime.CreateProxyFor<IAvaloniaNativeFactory>(factory, true);

            AvaloniaLocator.CurrentMutable.Bind<IAvaloniaNativeFactory>().ToConstant(factoryProxy);

            var result = new AvaloniaNativePlatform(factoryProxy);

            result.DoInitialize(options);

            return result;
        }

        public static AvaloniaNativePlatform Initialize(AvaloniaNativePlatformOptions options)
        {
            if (options.AvaloniaNativeLibraryPath != null)
            {
                var lib = NativeLibrary.Load(options.AvaloniaNativeLibraryPath);
                if (!NativeLibrary.TryGetExport(lib, "CreateAvaloniaNative", out var proc))
                    throw new InvalidOperationException(
                        "Unable to get \"CreateAvaloniaNative\" export from AvaloniaNativeLibrary library");
                var d = Marshal.GetDelegateForFunctionPointer<CreateAvaloniaNativeDelegate>(proc);

                return Initialize(d(), options);
            }

            return Initialize(CreateAvaloniaNative(), options);
        }

        public void SetupApplicationMenuExporter()
        {
            _ = new AvaloniaNativeMenuExporter(_factory);
        }

        public void SetupApplicationDockMenuExporter()
        {
            _ = new AvaloniaNativeMenuExporter(_factory, AvaloniaNativeMenuExporter.MenuTarget.Dock);
        }

        public void SetupApplicationName()
        {
            if (!string.IsNullOrWhiteSpace(Application.Current!.Name))
                _factory.MacOptions.SetApplicationTitle(Application.Current.Name);
        }

        private void DoInitialize(AvaloniaNativePlatformOptions options)
        {
            _options = options;

            var applicationPlatform = new AvaloniaNativeApplicationPlatform(this);

            var macOpts = AvaloniaLocator.Current.GetService<MacOSPlatformOptions>() ?? new MacOSPlatformOptions();

            _factory.MacOptions?.SetDisableAppDelegate(macOpts.DisableAvaloniaAppDelegate ? 1 : 0);

            _factory.Initialize(new GcHandleDeallocator(), applicationPlatform, new AvnDispatcher());

            if (_factory.MacOptions != null)
            {
                _factory.MacOptions.SetShowInDock(macOpts.ShowInDock ? 1 : 0);
                _factory.MacOptions.SetDisableSetProcessName(macOpts.DisableSetProcessName ? 1 : 0);
            }

            var clipboardImpl = new ClipboardImpl(_factory.CreateClipboard());
            var clipboard = new Clipboard(clipboardImpl);

            Dispatcher.InitializeUIThreadDispatcher(new DispatcherImpl(_factory.CreatePlatformThreadingInterface()));
            AvaloniaLocator.CurrentMutable
                .Bind<ICursorFactory>().ToConstant(new CursorFactory(_factory.CreateCursorFactory()))
                .Bind<IScreenImpl>().ToConstant(new ScreenImpl(_factory.CreateScreens))
                .Bind<IPlatformIconLoader>().ToSingleton<IconLoader>()
                .Bind<IKeyboardDevice>().ToConstant(SKeyboardDevice)
                .Bind<IPlatformSettings>().ToConstant(new NativePlatformSettings(_factory.CreatePlatformSettings()))
                .Bind<IWindowingPlatform>().ToConstant(this)
                .Bind<IClipboardImpl>().ToConstant(clipboardImpl)
                .Bind<IClipboard>().ToConstant(clipboard)
                .Bind<IRenderLoop>().ToConstant(RenderLoop.FromTimer(
                    new ThreadProxyRenderTimer(new AvaloniaNativeRenderTimer(_factory.CreatePlatformRenderTimer()))))
                .Bind<IMountedVolumeInfoProvider>().ToConstant(new MacOsMountedVolumeInfoProvider())
                .Bind<IPlatformDragSource>().ToConstant(new AvaloniaNativeDragSource(_factory))
                .Bind<IPlatformLifetimeEventsImpl>().ToConstant(applicationPlatform)
                .Bind<INativeApplicationCommands>()
                .ToConstant(new MacOsNativeMenuCommands(_factory.CreateApplicationCommands()))
                .Bind<IActivatableLifetime>().ToSingleton<MacOsActivatableLifetime>()
                .Bind<IStorageProviderFactory>()
                .ToConstant(new StorageProviderApi(_factory.CreateStorageProvider(), options.AppSandboxEnabled));

            var hotkeys =
                new PlatformHotkeyConfiguration(KeyModifiers.Meta, wholeWordTextActionModifiers: KeyModifiers.Alt);
            hotkeys.MoveCursorToTheStartOfLine.Add(new(Key.Left, hotkeys.CommandModifiers));
            hotkeys.MoveCursorToTheStartOfLineWithSelection.Add(new(Key.Left,
                hotkeys.CommandModifiers | hotkeys.SelectionModifiers));
            hotkeys.MoveCursorToTheEndOfLine.Add(new(Key.Right, hotkeys.CommandModifiers));
            hotkeys.MoveCursorToTheEndOfLineWithSelection.Add(new(Key.Right,
                hotkeys.CommandModifiers | hotkeys.SelectionModifiers));

            AvaloniaLocator.CurrentMutable.Bind<PlatformHotkeyConfiguration>().ToConstant(hotkeys);

            AvaloniaLocator.CurrentMutable.Bind<KeyGestureFormatInfo>().ToConstant(new KeyGestureFormatInfo(
                new Dictionary<Key, string>
                {
                    { Key.Back, "⌫" }, { Key.Down, "↓" }, { Key.End, "↘" }, { Key.Escape, "⎋" },
                    { Key.Home, "↖" }, { Key.Left, "←" }, { Key.Return, "↩" }, { Key.PageDown, "⇟" },
                    { Key.PageUp, "⇞" }, { Key.Right, "→" }, { Key.Space, "␣" }, { Key.Tab, "⇥" },
                    { Key.Up, "↑" },
                }, ctrl: "⌃", meta: "⌘", shift: "⇧", alt: "⌥"));

            foreach (var mode in _options.RenderingMode)
                if (mode == AvaloniaNativeRenderingMode.OpenGl)
                    try
                    {
                        _platformGraphics = new AvaloniaNativeGlPlatformGraphics(_factory.ObtainGlDisplay(), _factory);
                        break;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                else if (mode == AvaloniaNativeRenderingMode.Metal)
                    try
                    {
                        var metal = new MetalPlatformGraphics(_factory);
                        metal.CreateContext().Dispose();
                        _platformGraphics = metal;
                        break;
                    }
                    catch
                    {
                        // Ignored
                    }
                else if (mode == AvaloniaNativeRenderingMode.Software)
                    break;

            if (_platformGraphics != null)
                AvaloniaLocator.CurrentMutable
                    .Bind<IPlatformGraphics>().ToConstant(_platformGraphics);


            Compositor = new(_platformGraphics, true);
            AvaloniaLocator.CurrentMutable.Bind<Compositor>().ToConstant(Compositor);

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            // Self-dispose on exit, ensuring COM objects are properly released before the CLR shuts down.
            Dispose();
        }

        private class GcHandleDeallocator : NativeCallbackBase, IAvnGCHandleDeallocatorCallback
        {
            public void FreeGCHandle(IntPtr handle)
            {
                GCHandle.FromIntPtr(handle).Free();
            }
        }

        private delegate IntPtr CreateAvaloniaNativeDelegate();
    }
}
