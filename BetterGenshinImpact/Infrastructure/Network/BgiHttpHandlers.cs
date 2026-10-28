using System;
using System.Net;
using System.Net.Http;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 统一的主处理器：所有命名客户端都通过 <see cref="AppWebProxy"/> 决定代理，并使用相同的连接池策略
/// </summary>
public static class BgiHttpHandlers
{
    /// <summary>
    /// 创建主处理器
    /// </summary>
    /// <param name="proxy">程序代理</param>
    /// <param name="automaticDecompression">是否自动解压缩；断点续传下载需要关闭</param>
    /// <param name="allowAutoRedirect">是否自动跟随重定向；需要逐跳校验地址或请求带密钥时关闭</param>
    public static SocketsHttpHandler CreatePrimary(IWebProxy proxy, bool automaticDecompression = true, bool allowAutoRedirect = true) => new()
    {
        Proxy = proxy,
        UseProxy = true,
        AutomaticDecompression = automaticDecompression ? DecompressionMethods.All : DecompressionMethods.None,
        AllowAutoRedirect = allowAutoRedirect,
        MaxAutomaticRedirections = 10,
        // 定期重建连接，DNS 变化和代理切换后旧连接不会一直复用
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(15),
        // 处理器在同名客户端之间共享，不保存服务器下发的 Cookie，避免不同请求互相影响；手动设置的 Cookie 头照常发送
        UseCookies = false,
    };
}
