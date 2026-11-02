using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static readonly ILogger NetworkLogger = LogHub.GetLogger("Desktop.Network");
        private NetworkPreferences? _pendingNetwork;

        internal Func<AppSettings>? CurrentSettingsSnapshot { private get; init; }
        internal Func<NetworkPreferences>? CurrentNetworkPreferences { private get; init; }

        internal Func<NetworkPreferences, string, CancellationToken, Task<NetworkPreferences>>? CommitNetworkPreferences
        {
            private get;
            init;
        }

        private NetworkPreferences ActiveNetworkPreferences => CurrentNetworkPreferences?.Invoke() ??
                                                               _appliedSettings?.Network ?? _draft?.Network ?? new();

        private event Action? NetworkStateChanged;

        internal void RefreshNetworkPreferences(NetworkPreferences preferences)
        {
            if (_draft is null) return;
            _draft.Network = preferences;
            if (_appliedSettings is not null) _appliedSettings = _appliedSettings with { Network = preferences };
            var applying = _applyingLiveSettings;
            _applyingLiveSettings = true;
            try
            {
                NetworkStateChanged?.Invoke();
            }
            finally
            {
                _applyingLiveSettings = applying;
            }

            RefreshDefaultPresentation();
        }

        private Grid BuildNetworkSettings(AppSettings draft)
        {
            var protocol = new ComboBox
            {
                Name = "NetworkProxyMode", Width = 132,
                ItemsSource = new[] { UiText.Get("NetworkProxyNone"), "HTTP", "Socks5" },
                SelectedIndex = (int)(_pendingNetwork ?? draft.Network).Proxy,
            };
            var configure = new Button
                { Name = "NetworkProxyConfigure", [!ContentProperty] = LiveText.Resource("NetworkProxyConfigure") };
            var pending = new TextBlock
            {
                Name = "NetworkProxyPending", [!TextBlock.TextProperty] = LiveText.Resource("NetworkProxyPending"),
                VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.Muted,
                IsVisible = _pendingNetwork is not null,
            };
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Children = { protocol, configure, pending },
            };
            var refreshing = false;
            configure.Click += async (_, _) => await EditAsync((_pendingNetwork ?? draft.Network).Proxy);
            protocol.SelectionChanged += async (_, _) =>
            {
                if (refreshing || protocol.SelectedIndex < 0) return;
                configure.IsEnabled = protocol.IsEnabled = false;
                try
                {
                    if (protocol.SelectedIndex == 0)
                    {
                        _pendingNetwork = null;
                        if (ActiveNetworkPreferences.Proxy != NetworkProxyKind.None)
                            draft.Network = await CommitNetworkAsync(new(), "", ContentCancellationToken);
                        else draft.Network = new();
                        RequestLiveApply();
                    }
                    else
                    {
                        await EditNetworkProxyAsync(draft, (NetworkProxyKind)protocol.SelectedIndex);
                    }
                }
                catch (OperationCanceledException) when (ContentCancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    NetworkLogger.ZLogWarning(
                        $"Proxy configuration change failed. ExceptionType={error.GetType().Name}");
                    _error.Text = UiText.Get("NetworkProxySaveFailed");
                }
                finally
                {
                    Refresh();
                }
            };
            NetworkStateChanged += Refresh;
            RegisterContentCleanup(() => NetworkStateChanged -= Refresh);
            SubscribeContentLanguage(() =>
            {
                refreshing = true;
                var selectedIndex = protocol.SelectedIndex;
                protocol.ItemsSource = new[] { UiText.Get("NetworkProxyNone"), "HTTP", "Socks5" };
                protocol.SelectedIndex = selectedIndex;
                refreshing = false;
            });
            return SettingRow("NetworkProxyHeading", controls, static settings => settings.Network);

            void Refresh()
            {
                refreshing = true;
                protocol.SelectedIndex = (int)(_pendingNetwork ?? draft.Network).Proxy;
                pending.IsVisible = _pendingNetwork is not null;
                refreshing = false;
                configure.IsEnabled = protocol.IsEnabled = true;
            }

            async Task EditAsync(NetworkProxyKind selected)
            {
                configure.IsEnabled = protocol.IsEnabled = false;
                try
                {
                    await EditNetworkProxyAsync(draft,
                        selected == NetworkProxyKind.None ? NetworkProxyKind.Socks5 : selected);
                }
                finally
                {
                    Refresh();
                }
            }
        }

        private Task<NetworkPreferences> CommitNetworkAsync(NetworkPreferences preferences, string password,
            CancellationToken token)
        {
            return CommitNetworkPreferences?.Invoke(preferences, password, token)
                   ?? throw new InvalidOperationException("The settings host cannot commit network preferences.");
        }

        private async Task EditNetworkProxyAsync(AppSettings draft, NetworkProxyKind selected)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ContentCancellationToken);
            var token = lifetime.Token;
            var preferences = _pendingNetwork ?? draft.Network;
            var dialog = new Window
            {
                Name = "NetworkProxyEditor", Title = UiText.Get("NetworkProxyHeading"),
                Width = 440, MinWidth = 380, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            };
            dialog.Classes.Add("settings-window");
            dialog.Bind(TitleProperty, LiveText.Resource("NetworkProxyHeading"));
            var committing = false;
            var closeRequested = false;
            dialog.Closing += (_, args) =>
            {
                if (!committing) return;
                args.Cancel = true;
                closeRequested = true;
                // ReSharper disable once AccessToDisposedClosure
                lifetime.Cancel();
            };
            // ReSharper disable once AccessToDisposedClosure
            dialog.Closed += (_, _) => lifetime.Cancel();
            var type = new ComboBox
            {
                Name = "NetworkProxyType", ItemsSource = new[] { "Socks5", "HTTP" },
                SelectedIndex = selected == NetworkProxyKind.Http ? 1 : 0,
            };
            var host = new TextBox { Name = "NetworkProxyHost", Text = preferences.Host };
            var port = new TextBox
            {
                Name = "NetworkProxyPort", Width = 88,
                Text = preferences.Port > 0 ? preferences.Port.ToString(CultureInfo.InvariantCulture) : "",
            };
            var address = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
            address.Children.Add(host);
            Grid.SetColumn(port, 1);
            address.Children.Add(port);
            var username = new TextBox { Name = "NetworkProxyUsername", Text = preferences.Username };
            var password = new TextBox { Name = "NetworkProxyPassword", PasswordChar = '●' };
            var verify = new Button
            {
                Name = "NetworkProxyVerify", [!ContentProperty] = LiveText.Resource("NetworkProxyVerify"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var status = new TextBlock
                { Name = "NetworkProxyStatus", TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var page = new StackPanel { Spacing = 8, Margin = new(12) };
            page.Children.Add(Labeled("NetworkProxyType", type));
            page.Children.Add(Labeled("NetworkProxyAddress", address));
            page.Children.Add(Labeled("NetworkProxyUsername", username));
            page.Children.Add(Labeled("NetworkProxyPassword", password));
            page.Children.Add(status);
            page.Children.Add(verify);
            dialog.Content = page;
            verify.Click += async (_, _) =>
            {
                if (!int.TryParse(port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var portNumber))
                {
                    status.Text = UiText.Get("NetworkProxyInvalid");
                    return;
                }

                var candidate = new NetworkPreferences
                {
                    Proxy = type.SelectedIndex == 0 ? NetworkProxyKind.Socks5 : NetworkProxyKind.Http,
                    Host = host.Text?.Trim() ?? "", Port = portNumber, Username = username.Text ?? "",
                };
                try
                {
                    candidate.Validate();
                }
                catch (ArgumentException)
                {
                    status.Text = UiText.Get("NetworkProxyInvalid");
                    return;
                }

                var secret = password.Text ?? "";
                page.IsEnabled = false;
                status.Text = UiText.Get("NetworkProxyVerifying");
                var verified = false;
                try
                {
                    await ApplicationHttp.VerifyAsync(candidate, secret, token);
                    verified = true;
                    committing = true;
                    var committed = await CommitNetworkAsync(candidate, secret, token);
                    draft.Network = committed;
                    _pendingNetwork = null;
                    RequestLiveApply();
                    committing = false;
                    if (!token.IsCancellationRequested || closeRequested) dialog.Close(true);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    NetworkLogger.ZLogWarning(
                        $"Proxy verification or persistence failed. ExceptionType={error.GetType().Name}");
                    if (!token.IsCancellationRequested)
                        status.Text = UiText.Get(verified ? "NetworkProxySaveFailed" : "NetworkProxyFailed");
                }
                finally
                {
                    committing = false;
                    if (closeRequested && dialog.IsVisible) dialog.Close(false);
                    else if (!token.IsCancellationRequested) page.IsEnabled = true;
                }
            };
            dialog.Opened += async (_, _) =>
            {
                if (!preferences.HasPassword) return;
                try
                {
                    var secret = await NetworkCredentialStore.Read(preferences, token);
                    if (!token.IsCancellationRequested && string.IsNullOrEmpty(password.Text))
                    {
                        password.Text = secret ?? "";
                        if (secret is null) status.Text = UiText.Get("NetworkProxyCredentialUnavailable");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    NetworkLogger.ZLogWarning($"Proxy credential read failed. ExceptionType={error.GetType().Name}");
                    if (!token.IsCancellationRequested) status.Text = UiText.Get("NetworkProxyCredentialUnavailable");
                }
            };
            await dialog.ShowDialog<bool>(this);
        }
    }
}
