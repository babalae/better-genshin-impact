using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// Discord 机器人推送目标类型。
/// </summary>
public static class DiscordBotTargetTypes
{
    /// <summary>
    /// 推送到频道
    /// </summary>
    public const string Channel = "Channel";

    /// <summary>
    /// 私信推送给用户
    /// </summary>
    public const string DirectMessage = "DirectMessage";

    /// <summary>
    /// 判断目标类型是否为私信。配置可能来自旧版本或被手工修改，因此忽略大小写。
    /// </summary>
    public static bool IsDirectMessage(string? type)
    {
        return string.Equals(type, DirectMessage, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Discord 机器人推送目标：一个频道或一个私信对象。
/// 会作为配置项序列化进 config.json，因此属性保持简单可读。
/// </summary>
public partial class DiscordBotTarget : ObservableObject
{
    /// <summary>
    /// 目标类型，取值见 <see cref="DiscordBotTargetTypes"/>
    /// </summary>
    [ObservableProperty] private string _type = DiscordBotTargetTypes.Channel;

    /// <summary>
    /// 频道 ID（频道目标）或用户 ID（私信目标）
    /// </summary>
    [ObservableProperty] private string _id = string.Empty;

    /// <summary>
    /// 显示名称，例如「服务器名 / #频道名」或「服务器名 / @用户」。
    /// 仅用于界面显示，推送时不使用。
    /// </summary>
    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>
    /// 供配置反序列化使用的无参构造函数。
    /// </summary>
    public DiscordBotTarget()
    {
    }

    /// <summary>
    /// 按目标类型、ID 与显示名创建目标。
    /// </summary>
    public DiscordBotTarget(string type, string id, string displayName)
    {
        _type = type;
        _id = id;
        _displayName = displayName;
    }

    /// <summary>
    /// 复制一份，供挑选用对话框在副本上编辑，取消时不污染原配置。
    /// </summary>
    public DiscordBotTarget Clone()
    {
        return new DiscordBotTarget(Type, Id, DisplayName);
    }
}
