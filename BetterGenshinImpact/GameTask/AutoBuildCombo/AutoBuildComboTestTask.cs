using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using CsTrees;
using CsTrees.Blackboard;
using CsTrees.Display;
using CsTrees.Visitors;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.AutoFight.AutoFightTask;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 已构建行为树的运行时持有者：建树任务完成后暂存树根，
/// 供测试按钮启动/暂停 Tick 循环（暂停只中断循环，树节点状态保留可继续）
/// </summary>
public static class AutoBuildComboRuntime
{
    /// <summary>最近一次建树任务产出的行为树根节点，未建树时为 null</summary>
    public static Behaviour? Root { get; set; }

    /// <summary>建树任务使用的黑板，供手工扩展树时复用</summary>
    public static Blackboard? Blackboard { get; set; }
}

/// <summary>
/// 连招行为树测试任务：循环 Tick 已构建的行为树驱动战斗，取消即暂停
/// </summary>
public class AutoBuildComboTestTask : ISoloTask
{
    public string Name => "连招行为树测试";

    /// <summary>手工扩展后的树根缓存，避免任务重入时重复扩展包装</summary>
    private static Behaviour? _extendedRoot;

    /// <summary>扩展时的内层树引用，用于失效判断：建树任务重新建树后重新扩展</summary>
    private static Behaviour? _extendedSource;

    public async Task Start(CancellationToken ct)
    {
        var comboTree = AutoBuildComboRuntime.Root
            ?? throw new NormalEndException("尚未构建行为树，请先运行一次自动连招任务完成建树");

        // 重入时复用已扩展的树（节点状态跨暂停保留）；建树任务重新建树（Root 引用变化）时才重新扩展
        if (_extendedRoot == null || !ReferenceEquals(_extendedSource, comboTree))
        {
            Logger.LogInformation("{Name}扩展行为树：包装 LLM 树与战斗结束检测", Name);
            _extendedSource = comboTree;
            _extendedRoot = new AutoBuildComboTestBuilder()
                .WithBlackboard(AutoBuildComboRuntime.Blackboard!)
                    .Sequence("-", memory: true)
                        .Leaf(() => comboTree)
                        .CheckFightFinish("战斗结束检测")
                    .End()
                .End().Build();
        }

        Logger.LogInformation("{Name}任务启动，持续 Tick 行为树", Name);

        var tree = new BehaviourTree(_extendedRoot);
        var snapshot = new SnapshotVisitor();
        tree.AddVisitor(snapshot);

        // 复用自动战斗配置的持续索敌开关：与 Tick 循环并发运行
        // 使用独立的 CancellationTokenSource，暂停/结束时先停索敌并等待其清理
        using var targetingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? targetingTask = null;
        if (TaskContext.Instance().Config.AutoFightConfig.EnableCombatTargeting)
        {
            targetingTask = Task.Run(async () =>
            {
                try
                {
                    await AvatarRecognition.ContinuousTargetingLoopAsync(targetingCts.Token, () => false);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Logger.LogError(e, "持续索敌循环异常");
                }
            }, targetingCts.Token);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await tree.Tick();

                // 只渲染本次 Tick 遍历的路径，未访问的子树折叠为占位符
                var path = Display.AsciiTree(
                    comboTree,
                    showOnlyVisited: true,
                    visited: snapshot.Visited,
                    previouslyVisited: snapshot.PreviouslyVisited);
                Logger.LogInformation("Tick {Count}：\n{Path}", tree.Count, path);

                // 树完成一轮评估（根节点非 Running）时稍作等待，避免空转
                if (tree.Root.Status != Status.Running)
                {
                    Sleep(200, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 暂停即取消，行为树节点状态保留
        }
        finally
        {
            // 暂停/结束时先停止索敌循环并等待其完成清理，避免其 finally 释放按键与后续操作冲突
            if (targetingTask != null)
            {
                await targetingCts.CancelAsync();
                try { await targetingTask; } catch (OperationCanceledException) { }
            }

            Logger.LogInformation("{Name}任务暂停，行为树状态已保留，可再次点击继续", Name);
        }
    }
}

/// <summary>
/// 战斗结束检测行为：直接复用 AutoFight 的 CheckFightFinish
/// 战斗结束时抛出 NormalEndException 终止任务
/// 节流周期取自动战斗配置的 CheckTime ，好像有些不对，将就吧
/// </summary>
public partial class CheckFightFinish : Behaviour
{
    [BlackboardKey(Access = Access.Read)]
    public BehaviourKeyAccess<CombatScenes> CombatScenes { get; private set; } = null!;

    /// <summary>上次完整检查时间（静态共享：多个检查节点实例共用同一节流周期）</summary>
    private static DateTime _lastCheckTime = DateTime.MinValue;

    private TaskFightFinishDetectConfig _detectConfig = null!;

    private CheckFightFinish(string name) : base(name)
    {
    }

    protected override void Initialize()
    {
        var param = new AutoFightParam();
        _detectConfig = new AutoFightTask.TaskFightFinishDetectConfig(param.FinishDetectConfig);
    }

    protected async override Task<Status> Update()
    {
        // 节流：未到 CheckTime 间隔直接视为未结束，避免树的高频 Tick 反复打开编队界面
        if ((DateTime.Now - _lastCheckTime).TotalSeconds < _detectConfig.CheckTime)
        {
            return Status.Failure;
        }

        _lastCheckTime = DateTime.Now;

        // 令牌与其他行为节点保持同源（BeforeTask 写入 Avatar.Ct 的那个）
        var avatar = CombatScenes.Get().GetAvatars().FirstOrDefault();
        var ct = avatar?.Ct ?? CancellationToken.None;
        if (await AutoFightTask.CheckFightFinish(_detectConfig, ct))
        {
            Logger.LogInformation("战斗结束检测确认战斗结束");
            throw new NormalEndException("战斗结束");
        }

        return Status.Failure;
    }
}
