using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using System;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 生成、持久化并校验 HTTP API 与 MCP 共用的访问令牌。
/// </summary>
public sealed class ExternalAccessTokenService(IConfigService configService)
{
    /// <summary>
    /// 获取当前令牌；令牌为空时生成并立即持久化。
    /// </summary>
    /// <returns>可用于 Bearer 鉴权的访问令牌。</returns>
    public string EnsureToken()
    {
        var externalAccessConfig = configService.Get().ExternalAccessConfig;
        if (!string.IsNullOrWhiteSpace(externalAccessConfig.AccessToken))
        {
            return externalAccessConfig.AccessToken;
        }

        return RegenerateToken();
    }

    /// <summary>
    /// 重新生成 256-bit Base64Url 访问令牌并立即持久化。
    /// </summary>
    /// <returns>新生成的访问令牌。</returns>
    public string RegenerateToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        configService.Get().ExternalAccessConfig.AccessToken = token;
        configService.Save();
        return token;
    }

    /// <summary>
    /// 使用固定时间比较校验 Bearer 请求头。
    /// </summary>
    /// <param name="authorizationHeader">完整的 Authorization 请求头。</param>
    /// <returns>请求头中的令牌是否有效。</returns>
    public bool IsAuthorized(string? authorizationHeader)
    {
        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedToken = configService.Get().ExternalAccessConfig.AccessToken;
        var suppliedToken = authorizationHeader[bearerPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(expectedToken) || suppliedToken.Length != expectedToken.Length)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
