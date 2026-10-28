using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 代理模式
/// </summary>
public enum NetworkProxyMode
{
    /// <summary>跟随系统代理，与改造前行为一致（默认）</summary>
    System,

    /// <summary>不使用代理，全部直连</summary>
    None,

    /// <summary>使用自定义代理地址</summary>
    Custom,
}

/// <summary>
/// 网络设置。所有实例共享，各实例启动时读取；修改后对本实例的新请求立即生效
/// </summary>
[Serializable]
public partial class NetworkConfig : ObservableObject
{
    /// <summary>
    /// 代理模式
    /// </summary>
    [ObservableProperty]
    private NetworkProxyMode _proxyMode = NetworkProxyMode.System;

    /// <summary>
    /// 自定义代理地址，支持 http:// 和 socks5://，例如 http://127.0.0.1:7890
    /// </summary>
    [ObservableProperty]
    private string _proxyAddress = string.Empty;

    /// <summary>
    /// 不走代理的地址，分号分隔，支持 * 通配；本机地址始终直连
    /// </summary>
    [ObservableProperty]
    private string _proxyBypassList = "localhost;127.*;10.*;192.168.*";
}
