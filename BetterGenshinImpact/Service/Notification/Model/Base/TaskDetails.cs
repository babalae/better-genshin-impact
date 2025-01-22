using System;

namespace BetterGenshinImpact.Service.Notification.Model.Base;

// TODO: 需要制定标准，目前瞎写的
public class TaskDetails
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartTime { get; set; } = DateTime.Now;
} 