using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Reactive;

namespace Avalonia.Native.Interop
{
    // ReSharper disable once UnusedType.Global
    internal partial interface IAvnMenuItem;
}

namespace Avalonia.Native.Interop.Impl
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once ClassNeverInstantiated.Global
    internal partial class __MicroComIAvnMenuItemProxy
    {
        private __MicroComIAvnMenuProxy? _subMenu;
        // ReSharper disable once CollectionNeverQueried.Local
        private readonly CompositeDisposable _propertyDisposables = new();
        private IDisposable? _currentActionDisposable;

        public NativeMenuItemBase ManagedMenuItem { get; set; }

        private void UpdateTitle(string? title)
        {
            if (OperatingSystem.IsMacOS())
                // macOS does not process access key markers, so remove them.
                title = AccessText.RemoveAccessKeyMarker(title);
            if (string.IsNullOrWhiteSpace(title)) title = "";
            SetTitle(title);
        }

        private void UpdateToolTip(string? toolTip)
        {
            SetToolTip(toolTip ?? "");
        }

        private void UpdateIsVisible(bool isVisible)
        {
            SetIsVisible(isVisible.AsComBool());
        }

        private void UpdateIsChecked(bool isChecked)
        {
            SetIsChecked(isChecked.AsComBool());
        }

        private void UpdateToggleType(MenuItemToggleType toggleType)
        {
            SetToggleType((AvnMenuItemToggleType)toggleType);
        }

        private unsafe void UpdateIcon(Bitmap? icon)
        {
            if (icon is null)
            {
                SetIcon(null, IntPtr.Zero);
            }
            else
            {
                using var ms = new MemoryStream();
                icon.Save(ms, PngBitmapEncoderOptions.Default);

                var imageData = ms.ToArray();

                fixed (void* ptr = imageData) SetIcon(ptr, new(imageData.Length));
            }
        }

        private void UpdateAction(NativeMenuItem? item)
        {
            _currentActionDisposable?.Dispose();

            var action = new PredicateCallback(() =>
            {
                if (item is not null && (item.Command is not null || item.HasClickHandlers)) return item.IsEnabled;

                return false;
            });

            var callback = new MenuActionCallback(() =>
            {
                (item as INativeMenuItemExporterEventsImplBridge)?.RaiseClicked();
            });

            _currentActionDisposable = Disposable.Create(() =>
            {
                action.Dispose();
                callback.Dispose();
            });

            SetAction(action, callback);
        }

        internal void Initialize(NativeMenuItemBase nativeMenuItem)
        {
            ManagedMenuItem = nativeMenuItem;

            if (ManagedMenuItem is NativeMenuItem item)
            {
                UpdateTitle(item.Header);

                UpdateToolTip(item.ToolTip);

                UpdateGesture(item.Gesture);

                UpdateAction(ManagedMenuItem as NativeMenuItem);

                UpdateToggleType(item.ToggleType);

                UpdateIcon(item.Icon);

                UpdateIsChecked(item.IsChecked);

                UpdateIsVisible(item.IsVisible);

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.HeaderProperty)
                    .Subscribe(UpdateTitle));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.ToolTipProperty)
                    .Subscribe(UpdateToolTip));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.GestureProperty)
                    .Subscribe(UpdateGesture));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.CommandProperty)
                    .Subscribe(_ => UpdateAction(ManagedMenuItem as NativeMenuItem)));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.ToggleTypeProperty)
                    .Subscribe(UpdateToggleType));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.IsCheckedProperty)
                    .Subscribe(UpdateIsChecked));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.IsVisibleProperty)
                    .Subscribe(UpdateIsVisible));

                _propertyDisposables.Add(ManagedMenuItem.GetObservable(NativeMenuItem.IconProperty)
                    .Subscribe(UpdateIcon));
            }
        }

        internal void Deinitialize()
        {
            if (_subMenu != null)
            {
                SetSubMenu(null);
                _subMenu.Deinitialise();
                _subMenu.Dispose();
                _subMenu = null;
            }

            _propertyDisposables.Dispose();
            _currentActionDisposable?.Dispose();
        }

        internal void Update(AvaloniaNativeMenuExporter exporter, IAvaloniaNativeFactory factory, NativeMenuItem item)
        {
            if (item != ManagedMenuItem)
                throw new ArgumentException("The item does not match the menuitem being updated.", nameof(item));

            if (item.Menu != null)
            {
                if (_subMenu == null)
                {
                    _subMenu = __MicroComIAvnMenuProxy.Create(factory);

                    if (item.Menu.GetValue(MacOsNativeMenuCommands.IsServicesSubmenuProperty))
                        factory.SetServicesMenu(_subMenu);

                    _subMenu.Initialize(exporter, item.Menu, item.Header);

                    SetSubMenu(_subMenu);
                }

                _subMenu.Update(factory, item.Menu);
            }

            if (item.Menu == null && _subMenu != null)
            {
                _subMenu.Deinitialise();
                _subMenu.Dispose();

                SetSubMenu(null);
            }
        }

        private void UpdateGesture(KeyGesture? gesture)
        {
            var modifiers = gesture == null
                ? AvnInputModifiers.AvnInputModifiersNone
                : (AvnInputModifiers)gesture.KeyModifiers;
            var key = gesture == null ? AvnKey.AvnKeyNone : (AvnKey)gesture.Key;
            SetGesture(key, modifiers);
        }
    }
}
