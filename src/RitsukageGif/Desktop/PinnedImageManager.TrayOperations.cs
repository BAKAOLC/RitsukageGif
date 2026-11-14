using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal int OpenWindowCount => AllWindows.Count();

        internal bool CanSaveAllPins => !_disposed && !_savingSelection &&
                                        AllWindows.Any() && AllWindows.All(window => window.CanSaveSelection);

        internal void SwitchToGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            SetActiveGroup(group);
        }

        internal void SetAllThumbnailDisplay(bool thumbnail)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.ToArray())
            {
                if (window is PinnedImageWindow image && _pendingRestore.ContainsKey(image))
                    image.PreparePendingThumbnailView();
                window.SetThumbnailDisplay(thumbnail, new Avalonia.PixelPoint(0, 0));
                RefreshPendingWindowState(window);
            }

            ScheduleSave();
        }

        internal void CloseAllPins(bool destroy)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.ToArray())
            {
                if (!AllWindows.Contains(window)) continue;
                if (destroy) window.DestroyForGroup();
                else CloseSelection(window, false);
            }
        }

        internal void EnsureAllPinsDisplayed()
        {
            Dispatcher.UIThread.VerifyAccess();
            RestoreMaskedPins();
            foreach (var pin in AllWindows)
            {
                if (pin is PinnedImageWindow image && _pendingRestore.ContainsKey(image))
                    image.PreparePendingThumbnailView();
                var window = pin.Window;
                window.UpdateLayout();
                var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
                if (screen is null) continue;
                const double inset = PinnedWindowPresentation.ShadeInset;
                var width = window.ClientSize.Width > 0 ? window.ClientSize.Width : window.Width;
                var height = window.ClientSize.Height > 0 ? window.ClientSize.Height : window.Height;
                var origin = window.PointToScreen(new(inset, inset));
                var bounds = new Avalonia.PixelRect(origin.X, origin.Y,
                    (int)Math.Round(Math.Max(0, width - inset * 2) * window.RenderScaling),
                    (int)Math.Round(Math.Max(0, height - inset * 2) * window.RenderScaling));
                var intersection = bounds.Intersect(screen.WorkingArea);
                if (intersection == bounds || intersection is { Width: >= 33, Height: >= 33 }) continue;
                if (window.Screens.Primary is not { } primary) continue;
                var target = primary.WorkingArea.Center;
                window.Position = new(window.Position.X + target.X - bounds.Center.X,
                    window.Position.Y + target.Y - bounds.Center.Y);
                switch (pin)
                {
                    case PinnedImageWindow restoredImage when _pendingRestore.TryGetValue(restoredImage, out var state):
                        state.X = window.Position.X;
                        state.Y = window.Position.Y;
                        break;
                    case PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved):
                        saved.Window.X = window.Position.X;
                        saved.Window.Y = window.Position.Y;
                        break;
                }
            }

            ScheduleSave();
        }

        internal async Task SaveAllPinsAsync(Window owner)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!CanSaveAllPins) return;
            _savingSelection = true;
            try
            {
                var preferences = SavePreferencesProvider().Normalize();
                var directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new()
                {
                    Title = UiText.Get("TrayNativePinSaveDirectory"), AllowMultiple = false,
                    SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(directory),
                });
                if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } selectedDirectory) return;
                if (_disposed) return;
                var pins = AllWindows.ToArray();
                if (pins.Length == 0 || pins.Any(pin => !pin.CanSaveSelection)) return;
                var stem = Path.GetFileNameWithoutExtension(preferences.CreateFileName(DateTime.Now,
                    StaticImageFormat.Png));
                var profile = new ScreenshotSaveProfile
                {
                    Format = StaticImageFormat.Png, ConflictPolicy = ScreenshotConflictPolicy.Fail,
                    Notify = false, Quality = preferences.Quality,
                };
                var mode = OutputColorModeProvider();
                for (var index = 0; index < pins.Length; index++)
                {
                    var token = pins[index].OutputLifetime;
                    var output = await pins[index].CaptureSceneOutputAsync(mode, token);
                    await SaveNativeBatchOutputAsync(output, selectedDirectory, $"{stem}_({index})", profile, token);
                }
            }
            finally
            {
                _savingSelection = false;
            }
        }

        private static async Task SaveNativeBatchOutputAsync(ScreenshotResult output, string directory,
            string stem, ScreenshotSaveProfile profile, CancellationToken token)
        {
            for (var suffix = 0;; suffix++)
            {
                var name = suffix == 0 ? stem : $"{stem}_{suffix}";
                var path = Path.Combine(directory, name + ".png");
                try
                {
                    await ScreenshotImageStorage.SaveAsync(output, path, profile, token);
                    return;
                }
                catch (IOException) when (output.ColorMode == OutputColorMode.Both
                                              ? Path.Exists(Path.Combine(directory, name + ".sdr.png")) ||
                                                Path.Exists(Path.Combine(directory, name + ".hdr.png"))
                                              : Path.Exists(path))
                {
                }
            }
        }
    }
}
