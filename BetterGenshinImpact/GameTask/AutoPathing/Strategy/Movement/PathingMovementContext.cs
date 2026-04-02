using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 移动模式的执行上下文 
/// Holds state and dependencies for movement actions across iterations
/// </summary>
public class PathingMovementContext
{
    public CancellationToken CancellationToken { get; set; }
    
    /// <summary>当前画面的图像区域</summary>
    public ImageRegion Screen { get; set; }
    
    /// <summary>当前循环尝试次数/时间参考</summary>
    public int Num { get; set; }
    
    /// <summary>距离目标点的距离</summary>
    public double Distance { get; set; }

    /// <summary>获取队伍配置</summary>
    public Func<PathingPartyConfig> PartyConfigGetter { get; set; }
    
    /// <summary>释放元素战技的回调方法</summary>
    public Func<Task> UseElementalSkillAction { get; set; }

    /// <summary>快速移动模式（加速跑等）启用状态</summary>
    public bool FastMode { get; set; }
    
    /// <summary>快速模式按键模拟上次触发时间</summary>
    public DateTime FastModeColdTime { get; set; }

    /// <summary>获取最后一次使用元素战技的时间</summary>
    public Func<DateTime> GetElementalSkillLastUseTime { get; set; }
    
    /// <summary>设置最后一次使用元素战技的时间</summary>
    public Action<DateTime> SetElementalSkillLastUseTime { get; set; }

    /// <summary>获取最后一次使用小道具的时间</summary>
    public Func<DateTime> GetUseGadgetLastUseTime { get; set; }
    
    /// <summary>设置最后一次使用小道具的时间</summary>
    public Action<DateTime> SetUseGadgetLastUseTime { get; set; }
}

