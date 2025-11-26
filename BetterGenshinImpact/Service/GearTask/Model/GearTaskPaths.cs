using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.GearTask.Model;

/// <summary>
/// 存储定义任务的路径信息
/// </summary>
public class GearTaskPaths
{
    private static readonly string TaskV2Path = Path.Combine(Global.Absolute("User"), "task_v2");
    
    public static readonly string TaskListPath = Path.Combine(TaskV2Path, "list");
    
    public static readonly string TaskTriggerPath = Path.Combine(TaskV2Path, "trigger");

}