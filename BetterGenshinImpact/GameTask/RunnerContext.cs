using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using Wpf.Ui.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 使用 TaskRunner 运行任务时的上下文
/// </summary>
public class RunnerContext : Singleton<RunnerContext>
{
    /// <summary>
    /// 是否是连续执行配置组的场景
    /// </summary>
    public bool IsContinuousRunGroup { get; set; }
    
    /// <summary>
    /// 暂停逻辑
    /// </summary>
    public bool IsSuspend { get; set; }
    
    /// <summary>
    /// 暂停实现
    /// </summary>
    public Dictionary<string, ISuspendable> SuspendableDictionary = new();

    /// <summary>
    /// 当前使用队伍名称
    /// 游戏内定义的队伍名称
    /// </summary>
    public string? PartyName { get; set; }

    /// <summary>
    /// 当前队伍角色信息
    /// </summary>
    private CombatScenes? _combatScenes;

    public async Task<CombatScenes?> GetCombatScenes(CancellationToken ct)
    {
        if (_combatScenes == null)
        {
            // 返回主界面再识别
            var returnMainUiTask = new ReturnMainUiTask();
            await returnMainUiTask.Start(ct);

            await Delay(200, ct);

            _combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (!_combatScenes.CheckTeamInitialized())
            {
                Logger.LogError("队伍角色识别失败");
                _combatScenes = null;
            }
        }

        return _combatScenes;
    }

    public void ClearCombatScenes()
    {
        _combatScenes = null;
    }

    /// <summary>
    /// 任务结束后的清理
    /// </summary>
    public void Clear()
    {
        // 连续执行配置组的情况下，不清理当前队伍
        if (!IsContinuousRunGroup)
        {
            PartyName = null;
        }

        _combatScenes = null;
        IsSuspend = false;
        SuspendableDictionary.Clear();
    }

    /// <summary>
    /// 彻底恢复到初始状态
    /// </summary>
    public void Reset()
    {
        IsContinuousRunGroup = false;
        PartyName = null;
        _combatScenes = null;
        IsSuspend = false;
        SuspendableDictionary.Clear();
    }
}