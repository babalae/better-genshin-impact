namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 任务计划的用途标记，仅用于界面归属分组，不参与执行逻辑。
/// </summary>
public enum PuloniaTaskPlanPurpose
{
    /// <summary>
    /// 普通计划，旧文件与新建计划的默认用途。
    /// </summary>
    General = 0,

    /// <summary>
    /// 一条龙计划，由一条龙界面创建和管理。
    /// </summary>
    OneDragon = 1
}
