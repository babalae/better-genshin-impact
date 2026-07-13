namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

public interface ISuspendable
{
    void Suspend();         // 暂停操作
    void Resume();          // 恢复操作
    bool IsSuspended { get; } // 是否处于暂停状态
}