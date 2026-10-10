using BetterGenshinImpact.Core.Config;
using LibGit2Sharp;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

namespace BetterGenshinImpact.Helpers.Http;

public sealed class ProxyService
{
    private static readonly Lazy<ProxyService> LazyInstance = new(() => new ProxyService(HttpClient.DefaultProxy));
    private static readonly Regex AddressPattern = new(
        @"\A(?:https?|socks5)://(?:\[[^\]\s]+\]|[^:/?#@\s\\]+):(?<port>[0-9]+)/?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly object _sync = new();
    private ProxySettingsSnapshot _current = new(null);
    private NetworkConfig? _config;

    public ProxyService(IWebProxy originalProxy)
    {
        OriginalProxy = originalProxy ?? throw new ArgumentNullException(nameof(originalProxy));
        Proxy = new DynamicWebProxy(this);
    }

    public static ProxyService Instance => LazyInstance.Value;

    public IWebProxy OriginalProxy { get; }
    public IWebProxy Proxy { get; }
    public ProxySettingsSnapshot Current => Volatile.Read(ref _current);
    public ProxySettingsSnapshot Startup { get; private set; } = new(null);
    public bool HasInvalidConfiguration { get; private set; }
    public bool RequiresWebViewRestart => Current != Startup;
    public event EventHandler? SettingsChanged;

    public void Initialize(NetworkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_sync)
        {
            if (_config != null)
            {
                return;
            }

            _config = config;
            ApplyConfiguration();
            // 整个进程共用启动快照，避免同一用户数据目录创建出参数不同的浏览器环境。
            Startup = Current;
            _config.PropertyChanged += OnConfigurationChanged;
        }
    }

    public void UseAsDefaultProxy()
    {
        HttpClient.DefaultProxy = Proxy;
    }

    public static bool TryParseAddress(string? value, out Uri? address)
    {
        address = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2048)
        {
            return false;
        }

        var match = AddressPattern.Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath is not ("" or "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        address = uri;
        return true;
    }

    public static string FormatAddress(Uri address)
    {
        var host = address.HostNameType == UriHostNameType.IPv6 ? $"[{address.IdnHost}]" : address.IdnHost;
        // Uri 会省略协议的默认端口，配置落盘时必须补回显式端口。
        return $"{address.Scheme}://{host}:{address.Port}";
    }

    public string GetWebViewBrowserArguments(string? existingArguments = null)
    {
        return Startup.GetBrowserArguments(existingArguments);
    }

    private void OnConfigurationChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_sync)
        {
            ApplyConfiguration();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyConfiguration()
    {
        var config = _config!;
        var valid = TryParseAddress(config.ProxyUrl, out var address);
        HasInvalidConfiguration = !valid;
        if (config.Enabled && !valid)
        {
            // 外部编辑的配置也不能启用非法代理；保留地址供用户在设置页修正。
            config.Enabled = false;
        }

        Volatile.Write(ref _current, new ProxySettingsSnapshot(config.Enabled && valid ? address : null));
    }

    private sealed class DynamicWebProxy : IWebProxy
    {
        private readonly ProxyService _service;
        private readonly OriginalProxyCredentials _originalCredentials;

        public DynamicWebProxy(ProxyService service)
        {
            _service = service;
            _originalCredentials = new OriginalProxyCredentials(service.OriginalProxy);
            Credentials = _originalCredentials;
        }

        public ICredentials? Credentials { get; set; }

        // 统一在 GetProxy 中完成旁路判断，只读一次配置，避免两次回调之间切换造成混用。
        public bool IsBypassed(Uri host) => false;

        public Uri? GetProxy(Uri destination)
        {
            var snapshot = _service.Current;
            if (snapshot.Address != null)
            {
                return snapshot.Address;
            }

            var original = _service.OriginalProxy;
            var proxy = original.IsBypassed(destination) ? destination : original.GetProxy(destination);
            if (proxy != null && proxy != destination)
            {
                _originalCredentials.Allow(proxy);
            }

            return proxy;
        }
    }

    private sealed class OriginalProxyCredentials(IWebProxy originalProxy) : ICredentials
    {
        private readonly ConcurrentDictionary<Uri, byte> _originalAddresses = new();

        public void Allow(Uri proxy) => _originalAddresses.TryAdd(proxy, 0);

        // 系统代理的凭据不能泄露给自定义代理；在途的系统代理请求仍可完成认证。
        public NetworkCredential? GetCredential(Uri uri, string authType) =>
            _originalAddresses.ContainsKey(uri) ? originalProxy.Credentials?.GetCredential(uri, authType) : null;
    }
}

public sealed record ProxySettingsSnapshot(Uri? Address)
{
    public bool Enabled => Address != null;
    public bool GitProxyUnsupported => Address != null && Address.Scheme != Uri.UriSchemeHttp;

    public ProxyOptions CreateGitProxyOptions() => new()
    {
        ProxyType = Address?.Scheme == Uri.UriSchemeHttp ? ProxyType.Specified : ProxyType.None,
        Url = Address?.Scheme == Uri.UriSchemeHttp ? ProxyService.FormatAddress(Address) : null
    };

    public void ValidateGitRemote(string remoteUrl)
    {
        // 当前 libgit2 仅对 HTTPS 远端建立代理隧道，不能让自定义 HTTP 仓库静默绕过代理。
        if (Address?.Scheme == Uri.UriSchemeHttp
            && Uri.TryCreate(remoteUrl, UriKind.Absolute, out var remote)
            && remote.Scheme == Uri.UriSchemeHttp)
        {
            throw new NotSupportedException("当前 Git 库仅支持通过 HTTP 代理同步 HTTPS 仓库。请将仓库地址改为 HTTPS，或关闭自定义代理后直连。");
        }
    }

    public string GetBrowserArguments(string? existingArguments = null)
    {
        var arguments = existingArguments ?? string.Empty;
        return Address == null
            ? arguments
            : $"{arguments} --proxy-server=\"{ProxyService.FormatAddress(Address)}\"".TrimStart();
    }
}
