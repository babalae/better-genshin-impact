using BetterGenshinImpact.Service.ExternalAccess.Models;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 与 HTTP、MCP 等传输协议无关的 BetterGI 外部访问能力。
/// </summary>
public interface IBgiExternalCapabilityService
{
    /// <summary>
    /// 获取当前 BetterGI 主实例的健康状态。
    /// </summary>
    /// <param name="cancellationToken">取消操作的令牌。</param>
    /// <returns>健康状态。</returns>
    Task<BgiHealthDto> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取 BetterGI 与外部访问 API 的版本信息。
    /// </summary>
    /// <param name="cancellationToken">取消操作的令牌。</param>
    /// <returns>版本信息。</returns>
    Task<BgiVersionDto> GetVersionAsync(CancellationToken cancellationToken);
}
