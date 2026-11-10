using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private NativeMenu CreatePinOperationsMenu()
        {
            var menu = new NativeMenu();
            Add("TrayNativePinAdjust", () =>
            {
                _pinnedImages.EnsureAllPinsDisplayed();
                return Task.CompletedTask;
            });
            Add("TrayNativePinThumbnailAll", () =>
            {
                _pinnedImages.SetAllThumbnailDisplay(true);
                return Task.CompletedTask;
            });
            Add("TrayNativePinThumbnailNone", () =>
            {
                _pinnedImages.SetAllThumbnailDisplay(false);
                return Task.CompletedTask;
            });
            Add("TrayNativePinSaveAll", () => _pinnedImages.SaveAllPinsAsync(_services.Context),
                _pinnedImages.CanSaveAllPins);
            Add("TrayNativePinCloseAll", () =>
            {
                _pinnedImages.CloseAllPins(false);
                return Task.CompletedTask;
            });
            Add("TrayNativePinHideShowAll", ToggleAllPinnedVisibilityAsync);
            Add("TrayNativePinDestroyAll", () =>
            {
                _pinnedImages.CloseAllPins(true);
                return Task.CompletedTask;
            });
            return menu;

            void Add(string key, Func<Task> action, bool available = true)
            {
                var item = new NativeMenuItem(UiText.Get(key))
                {
                    IsEnabled = available && !IsClosing && !IsSelecting,
                };
                item.Click += (_, _) => _ = RunTrayActionAsync(action);
                menu.Items.Add(item);
            }
        }

        private void DisposeTrayGroupIcons()
        {
            foreach (var icon in _trayGroupIcons) icon.Dispose();
            _trayGroupIcons.Clear();
        }
    }
}
