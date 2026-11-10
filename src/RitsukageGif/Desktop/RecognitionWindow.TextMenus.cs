using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecognitionWindow
    {
        private static Button TextMenuButton(string label, string icon, Func<ContextMenu> create)
        {
            var button = new Button
            {
                Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(4),
                Content = null,
            };
            button.Content = ToolbarIcon.Create(button, ToolbarIcon.FindDefinition(icon) ??
                                                        throw new InvalidOperationException(
                                                            "Recognition toolbar icon is not registered."));
            button.Bind(ToolTip.TipProperty, LiveText.Resource(label));
            button.Click += (_, _) => create().Open(button);
            return button;
        }

        private ContextMenu CreateTextLayoutMenu()
        {
            var menu = new ContextMenu();
            var layout = _dialogOptions.Layout ?? _preferences.Layout;
            var labels = new[] { "SystemOcrLayoutAuto", "SystemOcrLayoutSingle", "SystemOcrLayoutMulti" };
            for (var index = 0; index < labels.Length; index++)
            {
                var value = (RecognitionTextLayout)index;
                menu.Items.Add(TextRadio(labels[index], "text-layout", layout == value,
                    () => ChangeTextOptions(_dialogOptions with { Layout = value })));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(TextCheck("RecognitionRemoveSpaces", _dialogOptions.RemoveSpaces,
                value => ChangeTextOptions(_dialogOptions with { RemoveSpaces = value })));
            menu.Items.Add(TextCheck("RecognitionCopyOnClose", _dialogOptions.CopyTextOnClose,
                value => ChangeTextOptions(_dialogOptions with { CopyTextOnClose = value }, false)));
            menu.Items.Add(TextCheck("RecognitionHighlightSelected", _dialogOptions.HighlightSelectedText,
                value => ChangeTextOptions(_dialogOptions with { HighlightSelectedText = value }, false)));
            menu.Items.Add(new Separator());
            var punctuation = new MenuItem { Header = UiText.Get("SystemOcrPunctuation") };
            var punctuationLabels = new[]
            {
                "SystemOcrPunctuationNone", "SystemOcrPunctuationHalf", "SystemOcrPunctuationFull",
            };
            for (var index = 0; index < punctuationLabels.Length; index++)
            {
                var value = (RecognitionPunctuation)index;
                punctuation.Items.Add(TextRadio(punctuationLabels[index], "text-punctuation",
                    (_dialogOptions.Punctuation ?? _preferences.Punctuation) == value,
                    () => ChangeTextOptions(_dialogOptions with { Punctuation = value })));
            }

            menu.Items.Add(punctuation);
            var vertical = new MenuItem { Header = UiText.Get("RecognitionVerticalText") };
            vertical.Items.Add(TextRadio("RecognitionRightToLeft", "text-vertical", _dialogOptions.VerticalRightToLeft,
                () => ChangeTextOptions(_dialogOptions with { VerticalRightToLeft = true })));
            vertical.Items.Add(TextRadio("RecognitionLeftToRight", "text-vertical", !_dialogOptions.VerticalRightToLeft,
                () => ChangeTextOptions(_dialogOptions with { VerticalRightToLeft = false })));
            menu.Items.Add(vertical);
            return menu;
        }

        private ContextMenu CreateTextCopyMenu()
        {
            var menu = new ContextMenu();
            var close = new MenuItem { Header = UiText.Get("RecognitionAutoClose") };
            close.Items.Add(TextCheck("RecognitionCopyAllText", _dialogOptions.CloseAfterCopyAll,
                value => ChangeTextOptions(_dialogOptions with { CloseAfterCopyAll = value }, false)));
            close.Items.Add(TextCheck("RecognitionCopySelectedText", _dialogOptions.CloseAfterCopySelected,
                value => ChangeTextOptions(_dialogOptions with { CloseAfterCopySelected = value }, false)));
            menu.Items.Add(close);
            return menu;
        }

        private static MenuItem TextRadio(string label, string group, bool selected, Action action)
        {
            var item = new MenuItem
            {
                Header = UiText.Get(label), ToggleType = MenuItemToggleType.Radio, GroupName = group,
                IsChecked = selected,
            };
            item.Click += (_, _) => action();
            return item;
        }

        private static MenuItem TextCheck(string label, bool selected, Action<bool> action)
        {
            var item = new MenuItem
            {
                Header = UiText.Get(label), ToggleType = MenuItemToggleType.CheckBox, IsChecked = selected,
            };
            item.Click += (_, _) => action(item.IsChecked);
            return item;
        }

        private void ChangeTextOptions(RecognitionDialogPreferences edited, bool reformat = true)
        {
            var baseline = _dialogOptions;
            if (baseline == edited) return;
            _dialogOptions = edited;
            _dialogPreferencesChanged?.Invoke(baseline, edited);
            if (reformat) ApplyTextFormatting();
            else TextSelectionChanged();
        }
    }
}
