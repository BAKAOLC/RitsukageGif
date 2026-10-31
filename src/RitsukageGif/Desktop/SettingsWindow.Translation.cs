using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static readonly string[] TranslationProviderKeys =
        [
            "TranslationLibreTranslate", "TranslationDeepL", "TranslationAzure", "TranslationOpenAiCompatible",
        ];

        private static readonly ILogger TranslationLogger = LogHub.GetLogger("Desktop.TranslationSettings");

        private StackPanel BuildTranslationSettings(AppSettings draft)
        {
            var token = ContentCancellationToken;
            var page = Page();
            var preferences = draft.Translation.Normalize();
            var refreshingProvider = false;
            string? statusKey = null;
            var provider = new ComboBox
            {
                ItemsSource = TranslationProviderKeys.Select(UiText.Get).ToArray(),
                SelectedIndex = (int)preferences.Provider,
            };
            var endpoint = new TextBox { Text = preferences.Endpoint };
            var region = new TextBox { Text = preferences.Region };
            var model = new TextBox { Text = preferences.Model };
            var source = new TranslationLanguagePicker(true) { LanguageTag = preferences.SourceLanguage };
            var target = new TranslationLanguagePicker { LanguageTag = preferences.TargetLanguage };
            var fallback = new TranslationLanguagePicker(allowNone: true)
                { LanguageTag = preferences.FallbackLanguage };
            var timeout = new CompactNumberInput
                { Minimum = 1, Maximum = 300, Value = preferences.TimeoutSeconds, Width = 80 };
            var key = new TextBox { PasswordChar = '●' };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var useKey = SystemCheck("TranslationUseApiKey", preferences.UseApiKey,
                value => draft.Translation = draft.Translation with { UseApiKey = value });
            var save = new Button();
            var delete = new Button();
            save.Bind(ContentProperty, LiveText.Resource("TranslationSaveKey"));
            delete.Bind(ContentProperty, LiveText.Resource("TranslationDeleteKey"));
            var regionRow = Labeled("TranslationRegion", region);
            var modelRow = Labeled("TranslationModel", model);
            provider.SelectionChanged += (_, _) =>
            {
                if (refreshingProvider || provider.SelectedIndex < 0 ||
                    draft.Translation.Provider == (TranslationProvider)provider.SelectedIndex) return;
                draft.Translation = draft.Translation with { Provider = (TranslationProvider)provider.SelectedIndex };
                RefreshFields();
            };
            endpoint.TextChanged += (_, _) =>
                draft.Translation = draft.Translation with { Endpoint = endpoint.Text ?? "" };
            region.TextChanged += (_, _) => draft.Translation = draft.Translation with { Region = region.Text ?? "" };
            model.TextChanged += (_, _) => draft.Translation = draft.Translation with { Model = model.Text ?? "" };
            source.LanguageTagChanged += (_, _) =>
                draft.Translation = draft.Translation with { SourceLanguage = source.LanguageTag };
            target.LanguageTagChanged += (_, _) =>
                draft.Translation = draft.Translation with { TargetLanguage = target.LanguageTag };
            fallback.LanguageTagChanged += (_, _) =>
                draft.Translation = draft.Translation with { FallbackLanguage = fallback.LanguageTag };
            timeout.ValueChanged += (_, _) =>
                draft.Translation = draft.Translation with { TimeoutSeconds = (int)(timeout.Value ?? 30) };
            save.Click += async (_, _) => await StoreCredentialAsync(false);
            delete.Click += async (_, _) => await StoreCredentialAsync(true);
            page.Children.Add(Labeled("TranslationProvider", provider));
            page.Children.Add(Labeled("TranslationEndpoint", endpoint));
            page.Children.Add(regionRow);
            page.Children.Add(modelRow);
            page.Children.Add(Labeled("TranslationSourceLanguage", source));
            page.Children.Add(Labeled("TranslationTargetLanguage", target));
            page.Children.Add(Labeled("TranslationFallbackLanguage", fallback));
            page.Children.Add(Labeled("TranslationTimeout", timeout));
            page.Children.Add(useKey);
            page.Children.Add(Labeled("TranslationApiKey", key));
            page.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, delete },
            });
            page.Children.Add(status);
            RefreshFields();
            SubscribeContentLanguage(RefreshLanguage);
            return page;

            void RefreshLanguage()
            {
                var selected = provider.SelectedIndex;
                refreshingProvider = true;
                try
                {
                    provider.ItemsSource = TranslationProviderKeys.Select(UiText.Get).ToArray();
                    provider.SelectedIndex = selected;
                    if (statusKey is not null) status.Text = UiText.Get(statusKey);
                }
                finally
                {
                    refreshingProvider = false;
                }
            }

            void SetStatus(string? resourceKey)
            {
                statusKey = resourceKey;
                status.Text = resourceKey is null ? "" : UiText.Get(resourceKey);
            }

            void RefreshFields()
            {
                regionRow.IsVisible = provider.SelectedIndex == (int)TranslationProvider.Azure;
                modelRow.IsVisible = provider.SelectedIndex == (int)TranslationProvider.OpenAiCompatible;
                key.Text = "";
                SetStatus(null);
            }

            async Task StoreCredentialAsync(bool remove)
            {
                var snapshot = draft.Translation.Normalize();
                if (!Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                {
                    SetStatus("TranslationInvalidEndpoint");
                    return;
                }

                var secret = key.Text ?? "";
                if (!remove && string.IsNullOrWhiteSpace(secret))
                {
                    SetStatus("TranslationEnterKey");
                    return;
                }

                save.IsEnabled = delete.IsEnabled = false;
                try
                {
                    if (remove) await CredentialVault.DeleteAsync(snapshot.CredentialKey, token);
                    else await CredentialVault.WriteAsync(snapshot.CredentialKey, secret, token);
                    if (token.IsCancellationRequested) return;
                    if (snapshot.CredentialKey != draft.Translation.Normalize().CredentialKey) return;
                    useKey.IsChecked = !remove;
                    if (key.Text == secret) key.Text = "";
                    SetStatus(remove ? "TranslationKeyDeleted" : "TranslationKeySaved");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    TranslationLogger.ZLogWarning(error,
                        $"Translation credential operation failed: remove={remove}, exceptionType={error.GetType().Name}");
                    if (!token.IsCancellationRequested) SetStatus("TranslationCredentialUnavailable");
                }
                finally
                {
                    if (!token.IsCancellationRequested) save.IsEnabled = delete.IsEnabled = true;
                }
            }
        }
    }
}
