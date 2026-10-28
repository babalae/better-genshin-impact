using System;
using LibGit2Sharp;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 把网络设置套用到 LibGit2Sharp 的代理选项。
/// 只有自定义代理模式且仓库地址不在绕过列表时走代理；跟随系统和不使用代理两种模式都直连，与改造前克隆的行为一致
/// </summary>
public static class GitProxy
{
    /// <summary>
    /// 按网络设置修改代理选项
    /// </summary>
    /// <param name="options">FetchOptions、CloneOptions 等携带的代理选项</param>
    /// <param name="proxy">程序代理；为 null 时直连</param>
    /// <param name="repoUrl">仓库地址</param>
    public static void Apply(ProxyOptions options, AppWebProxy? proxy, string repoUrl)
    {
        options.ProxyType = ProxyType.None;
        options.Url = null;
        if (proxy is null || !Uri.TryCreate(repoUrl, UriKind.Absolute, out var repoUri))
        {
            return;
        }

        if (proxy.GetCustomProxy(repoUri) is { } proxyUri)
        {
            options.ProxyType = ProxyType.Specified;
            options.Url = proxyUri.ToString();
        }
    }

    /// <summary>
    /// 生成新的代理选项
    /// </summary>
    public static ProxyOptions Create(AppWebProxy? proxy, string repoUrl)
    {
        var options = new ProxyOptions();
        Apply(options, proxy, repoUrl);
        return options;
    }
}
