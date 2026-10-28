using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 按当前网络设置决定每个请求是否走代理。SocketsHttpHandler 每次请求都会询问 IWebProxy，
/// 所以修改设置后对新请求立即生效，不需要重建 HttpClient
/// </summary>
public sealed class AppWebProxy : IWebProxy
{
    /// <summary>
    /// 支持的代理协议
    /// </summary>
    private static readonly string[] SupportedSchemes = ["http", "https", "socks4", "socks4a", "socks5"];

    /// <summary>
    /// 地址必须形如 协议://主机:端口，主机可以是 [IPv6]
    /// </summary>
    private static readonly Regex ExplicitPortPattern =
        new(@"^[A-Za-z0-9]+://(\[[^\]]+\]|[^/:\[\]@]+):\d{1,5}/?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 网络设置
    /// </summary>
    private readonly NetworkConfig _config;

    /// <summary>
    /// 启动时取得的系统代理，“跟随系统”模式下使用
    /// </summary>
    private readonly IWebProxy? _systemProxy;

    /// <summary>
    /// 保护解析结果缓存
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 上次解析的代理地址文本，设置未变时复用解析结果
    /// </summary>
    private string? _parsedAddress;

    /// <summary>
    /// 解析后的代理地址；地址无效时为 null，即直连
    /// </summary>
    private Uri? _parsedProxy;

    /// <summary>
    /// 上次解析的绕过列表文本
    /// </summary>
    private string? _parsedBypassList;

    /// <summary>
    /// 绕过列表解析出的主机名匹配规则
    /// </summary>
    private Regex[] _bypassPatterns = [];

    /// <param name="config">网络设置</param>
    /// <param name="systemProxy">系统代理，通常为接管前的 HttpClient.DefaultProxy</param>
    public AppWebProxy(NetworkConfig config, IWebProxy? systemProxy)
    {
        _config = config;
        _systemProxy = systemProxy;
    }

    /// <summary>
    /// 当前代理模式
    /// </summary>
    public NetworkProxyMode Mode => _config.ProxyMode;

    /// <inheritdoc />
    public ICredentials? Credentials { get; set; }

    /// <inheritdoc />
    public Uri? GetProxy(Uri destination)
    {
        switch (_config.ProxyMode)
        {
            case NetworkProxyMode.None:
                return null;
            case NetworkProxyMode.Custom:
                return GetCustomProxy(destination);
            default:
                return _systemProxy?.IsBypassed(destination) == false ? _systemProxy.GetProxy(destination) : null;
        }
    }

    /// <inheritdoc />
    public bool IsBypassed(Uri host) => GetProxy(host) is null;

    /// <summary>
    /// 自定义代理模式下目标地址应使用的代理；不在自定义模式、地址无效或命中绕过列表时返回 null
    /// </summary>
    public Uri? GetCustomProxy(Uri destination)
    {
        if (_config.ProxyMode != NetworkProxyMode.Custom || destination.IsLoopback)
        {
            return null;
        }

        lock (_lock)
        {
            RefreshParsedSettings();
            if (_parsedProxy is null || _bypassPatterns.Any(pattern => pattern.IsMatch(destination.Host)))
            {
                return null;
            }

            return _parsedProxy;
        }
    }

    /// <summary>
    /// 校验代理地址：协议只能是 http、https、socks4、socks4a、socks5，并且必须写明主机和端口
    /// </summary>
    /// <param name="text">用户输入的地址</param>
    /// <param name="proxy">解析结果</param>
    /// <param name="error">不合法时的原因</param>
    public static bool TryParseProxyAddress(string? text, out Uri? proxy, out string error)
    {
        proxy = null;
        error = string.Empty;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "请填写代理地址";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !SupportedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = "地址格式不正确，示例：http://127.0.0.1:7890 或 socks5://127.0.0.1:1080";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "暂不支持需要用户名和密码的代理";
            return false;
        }

        if (!ExplicitPortPattern.IsMatch(trimmed))
        {
            error = "请写明端口号，例如 http://127.0.0.1:7890";
            return false;
        }

        proxy = new Uri($"{uri.Scheme}://{uri.Authority}");
        return true;
    }

    /// <summary>
    /// 设置变化时重新解析代理地址和绕过列表；地址无效时视为直连
    /// </summary>
    private void RefreshParsedSettings()
    {
        var address = _config.ProxyAddress;
        if (!string.Equals(address, _parsedAddress, StringComparison.Ordinal))
        {
            _parsedAddress = address;
            _parsedProxy = TryParseProxyAddress(address, out var proxy, out _) ? proxy : null;
        }

        var bypassList = _config.ProxyBypassList;
        if (!string.Equals(bypassList, _parsedBypassList, StringComparison.Ordinal))
        {
            _parsedBypassList = bypassList;
            _bypassPatterns = (bypassList ?? string.Empty)
                .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(pattern => new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
        }
    }
}
