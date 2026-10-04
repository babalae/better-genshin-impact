using System;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// Discord 通知方式。
/// Webhook 方式使用频道 Webhook 地址推送（<see cref="DiscordWebhookNotifier"/>），
/// Bot 方式使用机器人 Token + 频道 ID 推送（<see cref="DiscordBotNotifier"/>）。
/// </summary>
public static class DiscordNotificationModes
{
    /// <summary>
    /// 频道 Webhook 方式
    /// </summary>
    public const string Webhook = "Webhook";

    /// <summary>
    /// 机器人（Bot API）方式
    /// </summary>
    public const string Bot = "Bot";

    /// <summary>
    /// 判断配置值是否为机器人方式。
    /// 配置值可能来自旧版本配置文件或被用户手工修改，因此做忽略大小写的比较，
    /// 无法识别时回落到 Webhook 方式。
    /// </summary>
    public static bool IsBot(string? mode)
    {
        return string.Equals(mode, Bot, StringComparison.OrdinalIgnoreCase);
    }
}
