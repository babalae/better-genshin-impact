using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot 会话状态的独立持久化存储。
///
/// 存储位置：User/WechatClawbot/{SHA256(bot_token前16位hex)}.json
/// 文件格式：{"contextToken":"...", "getUpdatesBuf":"..."}
///
/// 线程安全（SemaphoreSlim），但进程内单进程独占。
/// 不写入 NotificationConfig，避免触发 AllConfig 的 PropertyChanged → Save() / RefreshNotifiers() 副作用。
/// 主实例写入（轮询循环中持久化最新游标/令牌），
/// 子实例只读（桌面分身等保留发送能力，从主实例写入的文件读取最新令牌）。
/// </summary>
public static class WechatClawbotSessionStore
{
    private static readonly ILogger Logger = App.GetLogger<LoggerHolder>();
    private sealed class LoggerHolder { }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record SessionState(string? ContextToken, string? GetUpdatesBuf);

    private static string DirectoryPath => Global.Absolute(Path.Combine("User", "WechatClawbot"));

    /// <summary>
    /// 按 bot token 生成稳定的文件名（用 SHA256 哈希避免 token 中的特殊字符）。
    /// </summary>
    private static string FilePathFor(string botToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(botToken))).ToLowerInvariant();
        return Path.Combine(DirectoryPath, $"{hash}.json");
    }

    /// <summary>
    /// 读取会话状态（从 JSON 文件）。文件不存在时返回空串。
    /// 主实例轮询启动时 + 子实例发送时调用。
    /// </summary>
    public static async Task<(string ContextToken, string GetUpdatesBuf)> LoadAsync(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return (string.Empty, string.Empty);

        await Gate.WaitAsync();
        try
        {
            var path = FilePathFor(botToken);
            if (!File.Exists(path))
                return (string.Empty, string.Empty);

            var json = await File.ReadAllTextAsync(path);
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
            return (state?.ContextToken ?? string.Empty, state?.GetUpdatesBuf ?? string.Empty);
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("读取微信 Clawbot 会话状态失败: {Ex}", ex.Message);
            return (string.Empty, string.Empty);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// 保存 context_token 与 get_updates_buf 到 JSON 文件。
    /// 仅在主实例的轮询循环中调用（token 变化或游标推进时）。
    /// 持久化失败时抛出异常，让调用方（绑定路径）感知落盘状态并决定是否继续。
    /// </summary>
    public static async Task SaveAsync(string botToken, string? contextToken, string? getUpdatesBuf)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return;

        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = FilePathFor(botToken);
            var state = new SessionState(contextToken, getUpdatesBuf);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        finally
        {
            Gate.Release();
        }
    }
}
