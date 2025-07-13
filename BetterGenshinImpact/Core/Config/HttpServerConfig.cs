using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// HTTP 服务器配置
/// </summary>
[Serializable]
public partial class HttpServerConfig : ObservableObject
{
    /// <summary>
    /// 是否启用 HTTP 服务器
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>
    /// HTTP 服务器端口
    /// </summary>
    [ObservableProperty]
    private int _port = 30648;

    /// <summary>
    /// 是否启用 CORS
    /// </summary>
    [ObservableProperty]
    private bool _enableCors = true;

    /// <summary>
    /// 是否启用 WebSocket 支持
    /// </summary>
    [ObservableProperty]
    private bool _enableWebSocket = true;

    /// <summary>
    /// API 访问令牌（可选，用于安全验证）
    /// </summary>
    [ObservableProperty]
    private string _accessToken = Guid.NewGuid().ToString("N");

    // /// <summary>
    // /// 是否启用 Swagger 文档
    // /// </summary>
    // [ObservableProperty]
    // private bool _enableSwagger = true;

    /// <summary>
    /// 监听地址
    /// </summary>
    [ObservableProperty]
    private string _host = "localhost";
}