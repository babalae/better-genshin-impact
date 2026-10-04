namespace BetterGenshinImpact.Service.ExternalAccess.Models;

/// <summary>
/// BetterGI 产品与外部访问协议版本信息。
/// </summary>
public sealed class BgiVersionDto
{
    /// <summary>
    /// 产品名称。
    /// </summary>
    public required string Product { get; init; }

    /// <summary>
    /// BetterGI 应用版本。
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 外部访问 API 版本。
    /// </summary>
    public required string ApiVersion { get; init; }
}
