using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// BetterGI 本机外部访问服务配置。
/// </summary>
[Serializable]
public partial class ExternalAccessConfig : ObservableValidator
{
    /// <summary>
    /// 是否启用 HTTP API。
    /// </summary>
    [ObservableProperty]
    private bool _httpApiEnabled;

    /// <summary>
    /// 是否启用 WebSocket 实时日志。
    /// </summary>
    [ObservableProperty]
    private bool _webSocketEnabled;

    /// <summary>
    /// 是否启用 MCP Streamable HTTP 服务。
    /// </summary>
    [ObservableProperty]
    private bool _mcpEnabled;

    /// <summary>
    /// 本机外部访问服务共享端口。
    /// </summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1024, 65535, ErrorMessage = "端口必须在 1024 到 65535 之间。")]
    private int _port = 30648;

    /// <summary>
    /// HTTP API 与 MCP 共用的 Bearer 访问令牌。
    /// </summary>
    [ObservableProperty]
    private string _accessToken = string.Empty;
}
