using BetterGenshinImpact.Service.ExternalAccess.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ExternalAccess.Mcp;

/// <summary>
/// 将共享 BetterGI 外部访问能力适配为 MCP 工具。
/// </summary>
[McpServerToolType]
public sealed class BgiMcpTools
{
    /// <summary>
    /// 与传输协议无关的共享能力服务。
    /// </summary>
    private readonly IBgiExternalCapabilityService _capabilityService;

    /// <summary>
    /// 创建 BetterGI MCP 工具适配器。
    /// </summary>
    /// <param name="capabilityService">共享能力服务。</param>
    public BgiMcpTools(IBgiExternalCapabilityService capabilityService)
    {
        _capabilityService = capabilityService;
    }

    /// <summary>
    /// 获取当前 BetterGI 主实例健康状态。
    /// </summary>
    /// <param name="cancellationToken">MCP 请求取消令牌。</param>
    /// <returns>共享健康状态 DTO。</returns>
    [McpServerTool(Name = "get_bgi_health", UseStructuredContent = true)]
    [Description("获取当前 BetterGI 主实例的健康状态、进程信息与运行时长。")]
    public Task<BgiHealthDto> GetHealthAsync(CancellationToken cancellationToken)
    {
        return _capabilityService.GetHealthAsync(cancellationToken);
    }

    /// <summary>
    /// 获取 BetterGI 与外部访问 API 版本。
    /// </summary>
    /// <param name="cancellationToken">MCP 请求取消令牌。</param>
    /// <returns>共享版本 DTO。</returns>
    [McpServerTool(Name = "get_bgi_version", UseStructuredContent = true)]
    [Description("获取 BetterGI 产品版本和外部访问 API 版本。")]
    public Task<BgiVersionDto> GetVersionAsync(CancellationToken cancellationToken)
    {
        return _capabilityService.GetVersionAsync(cancellationToken);
    }
}
