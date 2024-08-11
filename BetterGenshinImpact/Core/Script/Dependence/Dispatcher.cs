using System;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Dispatcher
{
    public void RunTask()
    {
    }

    public void AddTimer(RealTimeTimer timer)
    {
        if (string.IsNullOrEmpty(timer.Name))
        {
            throw new ArgumentNullException(nameof(timer.Name), "实时任务名称不能为空");
        }

        TaskTriggerDispatcher.Instance().AddTrigger(timer.Name, timer.Config);
    }
}
