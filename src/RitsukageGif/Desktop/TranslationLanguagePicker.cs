using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class TranslationLanguagePicker : ComboBox
    {
        public static readonly StyledProperty<string> LanguageTagProperty =
            AvaloniaProperty.Register<TranslationLanguagePicker, string>(nameof(LanguageTag), "");

        private static readonly (string Tag, string Key)[] Languages =
        [
            ("en", "TranslationLanguageEnglish"), ("zh", "TranslationLanguageChinese"),
            ("zh-Hans", "TranslationLanguageChineseSimplified"), ("zh-Hant", "TranslationLanguageChineseTraditional"),
            ("ja", "TranslationLanguageJapanese"), ("ko", "TranslationLanguageKorean"),
            ("fr", "TranslationLanguageFrench"), ("de", "TranslationLanguageGerman"),
            ("es", "TranslationLanguageSpanish"), ("it", "TranslationLanguageItalian"),
            ("pt", "TranslationLanguagePortuguese"), ("ru", "TranslationLanguageRussian"),
            ("ar", "TranslationLanguageArabic"), ("hi", "TranslationLanguageHindi"),
            ("nl", "TranslationLanguageDutch"), ("pl", "TranslationLanguagePolish"),
            ("tr", "TranslationLanguageTurkish"), ("uk", "TranslationLanguageUkrainian"),
            ("vi", "TranslationLanguageVietnamese"), ("th", "TranslationLanguageThai"),
        ];

        private readonly bool _allowAutomatic;
        private readonly bool _allowNone;
        private bool _attached;
        private bool _updating;

        public TranslationLanguagePicker(bool allowAutomatic = false, bool allowNone = false)
        {
            _allowAutomatic = allowAutomatic;
            _allowNone = allowNone;
            Width = 105;
            MinWidth = 0;
            SelectionChanged += (_, _) =>
            {
                if (!_updating && SelectedItem is ComboBoxItem { Tag: string tag }) LanguageTag = tag;
            };
            AttachedToVisualTree += (_, _) =>
            {
                _attached = true;
                UiText.LanguageChanged += LanguageChanged;
                RefreshItems();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _attached = false;
                UiText.LanguageChanged -= LanguageChanged;
            };
            RefreshItems();
        }

        protected override Type StyleKeyOverride => typeof(ComboBox);

        public string LanguageTag
        {
            get => GetValue(LanguageTagProperty);
            set => SetValue(LanguageTagProperty, value);
        }

        public event EventHandler? LanguageTagChanged;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != LanguageTagProperty) return;
            RefreshItems();
            LanguageTagChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LanguageChanged()
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshItems();
            else
                Dispatcher.UIThread.Post(() =>
                {
                    if (_attached) RefreshItems();
                });
        }

        private void RefreshItems()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var items = new List<ComboBoxItem>();
                if (_allowAutomatic) Add("auto", "TranslationLanguageAutomatic");
                if (_allowNone) Add("", "TranslationLanguageNone");
                foreach (var (tag, key) in Languages) Add(tag, key);
                var selected = items.FirstOrDefault(item =>
                    string.Equals((string)item.Tag!, LanguageTag, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    selected = new()
                    {
                        Tag = LanguageTag,
                        Content = UiText.Format("TranslationLanguageCustom", LanguageTag),
                    };
                    items.Add(selected);
                }

                ItemsSource = items;
                SelectedItem = selected;
                ToolTip.SetTip(this, selected.Content);
                return;

                void Add(string tag, string key)
                {
                    items.Add(new() { Tag = tag, Content = UiText.Get(key) });
                }
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
