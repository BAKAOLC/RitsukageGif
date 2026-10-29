using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private readonly Dictionary<(Control Control, AvaloniaProperty Property), object?> _liveInputChanges = [];
        private readonly List<string> _liveReadErrors = [];
        private AppSettings? _appliedSettings;
        private bool _applyingLiveSettings;
        private bool _explicitLiveChangePending;
        private bool _liveApplyQueued;
        private bool _liveChangesPending;

        private void SubscribeLiveSettings()
        {
            var token = ContentCancellationToken;
            Observe<TextBox>(TextBox.TextProperty);
            Observe<AutoCompleteBox>(AutoCompleteBox.TextProperty);
            Observe<NumericUpDown>(NumericUpDown.TextProperty);
            Observe<ToggleButton>(ToggleButton.IsCheckedProperty);
            Observe<SelectingItemsControl>(SelectingItemsControl.SelectedIndexProperty);
            Observe<NumericUpDown>(NumericUpDown.ValueProperty);
            Observe<ColorPicker>(ColorView.ColorProperty);
            Closing += ClosingSettings;
            RegisterContentCleanup(() => Closing -= ClosingSettings);
            return;

            void Observe<T>(AvaloniaProperty property) where T : Control
            {
                var subscription = property.Changed.AddClassHandler<T>((control, change) =>
                {
                    if (property == NumericUpDown.TextProperty && change.OldValue is null) return;
                    if (control is TextBox { TemplatedParent: not null }) return;
                    if (token.IsCancellationRequested || GetTopLevel(control) != this) return;
                    _liveInputChanges.TryAdd((control, property), change.OldValue);
                    RequestLiveApply(false);
                });
                RegisterContentCleanup(subscription.Dispose);
            }

            void ClosingSettings(object? sender, WindowClosingEventArgs args)
            {
                ApplyLiveSettings();
            }
        }

        private void RequestLiveApply(bool explicitChange = true)
        {
            if (_readDraft is null || _applyingLiveSettings) return;
            _explicitLiveChangePending |= explicitChange;
            if (_liveApplyQueued) return;
            var token = ContentCancellationToken;
            _liveChangesPending = true;
            _liveApplyQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                _liveApplyQueued = false;
                ApplyLiveSettings();
            });
        }

        private void ApplyLiveSettings()
        {
            if (_draft is null || _readDraft is null || _applyingLiveSettings || !_liveChangesPending) return;
            _liveApplyQueued = false;
            _liveChangesPending = false;
            var changed = _explicitLiveChangePending || _liveInputChanges.Any(pair =>
                !Equals(pair.Key.Control.GetValue(pair.Key.Property), pair.Value));
            _explicitLiveChangePending = false;
            _liveInputChanges.Clear();
            if (!changed) return;
            _applyingLiveSettings = true;
            try
            {
                _readDraft();
                var candidate = CloneDraft(_draft);
                var previous = _appliedSettings ?? candidate;
                var errors = new List<string>(_liveReadErrors);
                if (string.IsNullOrWhiteSpace(candidate.OutputDirectory))
                {
                    errors.Add(UiText.Get("OutputRequired"));
                    candidate.OutputDirectory = previous.OutputDirectory;
                }

                var localErrors = SelectionCommands.Validate(candidate.SelectionShortcuts);
                if (localErrors.Count > 0)
                {
                    errors.AddRange(localErrors);
                    candidate.SelectionShortcuts = previous.SelectionShortcuts;
                }

                var mouseErrors = MouseCommands.Validate(candidate.MouseShortcuts);
                if (mouseErrors.Count > 0)
                {
                    errors.AddRange(mouseErrors);
                    candidate.MouseShortcuts = previous.MouseShortcuts;
                }

                try
                {
                    HotkeyService.Validate(candidate.PresetHotkeys.Select(binding => binding.Shortcut));
                }
                catch (ArgumentException error)
                {
                    errors.Add(error.Message);
                    candidate.PresetHotkeys = previous.PresetHotkeys;
                }

                var actionErrors = GlobalActions.Validate(candidate.GlobalActions,
                    candidate.PresetHotkeys.Select(binding => binding.Shortcut), candidate.GlobalMouseBindings);
                var globalMouseErrors = GlobalMouseService.Validate(candidate.GlobalMouseBindings,
                    candidate.GlobalActions);
                if (actionErrors.Count > 0 || globalMouseErrors.Count > 0)
                {
                    errors.AddRange(actionErrors);
                    errors.AddRange(globalMouseErrors);
                    candidate.PresetHotkeys = previous.PresetHotkeys;
                    candidate.GlobalActions = previous.GlobalActions;
                    candidate.GlobalMouseBindings = previous.GlobalMouseBindings;
                }

                candidate.ScreenshotSave = candidate.ScreenshotSave.Normalize();
                if (!candidate.ContentEquals(_appliedSettings))
                {
                    ApplySettings?.Invoke(candidate);
                    _appliedSettings = CloneDraft(candidate);
                }

                _error.Text = string.Join(Environment.NewLine, errors.Distinct());
            }
            catch (Exception error)
            {
                _error.Text = error.Message;
            }
            finally
            {
                _applyingLiveSettings = false;
            }
        }

        private static decimal ReadSettingsNumber(CompactNumberInput control)
        {
            var valid = control.TryGetInputValue(out var value);
            return ValidateSettingsNumber(control, valid, value, control.Minimum, control.Maximum);
        }

        private static decimal ReadSettingsNumber(NumericUpDown control)
        {
            var value = control.Value ?? control.Minimum;
            var valid = (control.Text is null || decimal.TryParse(control.Text, NumberStyles.Number,
                control.NumberFormat, out value)) && value >= control.Minimum && value <= control.Maximum;
            return ValidateSettingsNumber(control, valid, value, control.Minimum, control.Maximum);
        }

        private static decimal ValidateSettingsNumber(Control control, bool valid, decimal value,
            decimal minimum, decimal maximum)
        {
            if (valid)
            {
                DataValidationErrors.SetErrors(control, null);
                return value;
            }

            var message = UiText.Format("SettingsInvalidNumber", minimum, maximum);
            DataValidationErrors.SetErrors(control, [message]);
            throw new ArgumentException(message);
        }

        private void ReadLiveSection(Action read)
        {
            try
            {
                read();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
            {
                _liveReadErrors.Add(error.Message);
            }
        }

        private void ReplaceLiveSettings(AppSettings settings)
        {
            var replacement = CloneDraft(settings);
            ApplySettings?.Invoke(replacement);
            BuildContent(CloneDraft(replacement), _currentPage);
        }
    }
}
