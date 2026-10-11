namespace BetterGenshinImpact.Service.Notifier.Exception;

/// <summary>
/// 微信 Clawbot 推送会话过期异常。
/// 触发条件：sendmessage 返回 ret=-2 且 errmsg 含 "prepare failed"，
/// 表示用户超过 12~24 小时未在微信中给 Clawbot 私聊，服务端拒绝推送。
/// 这不是客户端 token 过期，是微信 ClawBot 协议的服务端推送权限限制，
/// 用户需在微信侧给 Clawbot 私聊任意内容以重新激活推送通道。
/// </summary>
public sealed class WechatClawbotSessionExpiredException(string message) : NotifierException(message);
