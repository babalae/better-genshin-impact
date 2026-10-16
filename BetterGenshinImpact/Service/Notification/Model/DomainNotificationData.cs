using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Service.Notification.Model;

public class DomainNotificationData : BaseNotificationData
{
    public AutoDomainParam? Domain { get; set; }

    /// <summary>
    /// 创建秘境成功通知，将奖励复制为快照并附加到消息中；没有奖励时保留原消息。
    /// </summary>
    public static DomainNotificationData CreateSuccess(NotificationEvent notificationEvent, string message,
        IReadOnlyDictionary<string, int>? rewards)
    {
        // 通知异步发送，复制奖励数据，避免后续轮次或任务修改正在发送的内容。
        var snapshot = rewards is { Count: > 0 } ? new Dictionary<string, int>(rewards) : null;
        return new DomainNotificationData
        {
            Event = notificationEvent.Code,
            Result = NotificationEventResult.Success,
            Message = snapshot == null
                ? message
                : message + Environment.NewLine + string.Join(", ", snapshot.Select(r => $"{r.Key} x{r.Value}")),
            Data = snapshot
        };
    }
}
