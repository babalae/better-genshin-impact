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

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

/// <summary>
/// 已构建行为树的运行时持有者：建树任务完成后暂存建树会话，
/// 供测试按钮启动/暂停 Tick 循环（暂停只中断循环，下次启动重新 Build 复位行为状态）
/// </summary>
public static class AutoComboRuntime
{
    /// <summary>最近一次建树任务产出的建树会话，未建树时为 null</summary>
    public static ComboTreeSession? Session { get; set; }
}

/// <summary>
/// 连招行为树运行任务：循环 Tick 指定建树会话的行为树驱动战斗，取消即暂停
/// 每次启动都通过 Builder 重新 Build 出全新节点实例的树，天然完成行为状态复位
/// </summary>
public class AutoComboRunTask : ISoloTask
{
    public string Name => "自动连招运行";

    /// <summary>宿主场景的战斗意图：结束检测开关、拾取、超时等，任务按开关决定自身行为</summary>
    private readonly AutoFightParam _param;

    /// <summary>本次运行消费的建树会话：构造时强制注入，任务自身不读取任何静态状态</summary>
    private readonly ComboTreeSession _session;

    public AutoComboRunTask(AutoFightParam param, ComboTreeSession session)
    {
        _param = param;
        _session = session;
    }

    public async Task Start(CancellationToken ct)
    {
        var session = _session;

        // 标准消费者协议：重新识别队伍（顺带由 BindAndBuild 校验与建树队伍是否一致）→ BeforeTask 写入本任务令牌
        var combatScenes = CombatScenes.GetCombatScenesWithRetry();
        combatScenes.BeforeTask(ct);

        // 清黑板 → 授权写入 CombatScenes → 重新 Build 得到全新节点实例的行为树（行为状态复位）
        var comboTree = session.BindAndBuild(combatScenes);

        // 按宿主意图决定是否包装自带战斗结束检测：外部控制结束时（如秘境）关闭，只认取消令牌
        Behaviour extendedRoot;
        if (_param.FightFinishDetectEnabled)
        {
            Logger.LogInformation("{Name}扩展行为树：包装 LLM 树与战斗结束检测", Name);
            extendedRoot = new AutoComboRunBuilder()
                .WithBlackboard(session.Blackboard)
                    .Sequence("-", memory: true)
                        .Leaf(() => comboTree)
                        .CheckFightFinish("战斗结束检测")
                    .End()
                .End().Build();
        }
        else
        {
            Logger.LogInformation("{Name}使用宿主场景的结束控制，不包装战斗结束检测", Name);
            extendedRoot = comboTree;
        }

        Logger.LogInformation("{Name}任务启动，持续 Tick 行为树", Name);

        var tree = new BehaviourTree(extendedRoot);
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
            // 暂停即取消，下次启动重新 Build 复位行为状态
        }
        finally
        {
            // 暂停/结束时先停止索敌循环并等待其完成清理，避免其 finally 释放按键与后续操作冲突
            if (targetingTask != null)
            {
                await targetingCts.CancelAsync();
                try { await targetingTask; } catch (OperationCanceledException) { }
            }

            combatScenes.AfterTask();
            Logger.LogInformation("{Name}任务暂停，可再次点击继续", Name);
        }
    }
}

/// <summary>
/// 战斗结束检测行为：直接复用 AutoFight 的 CheckFightFinish
/// 战斗结束时抛出 NormalEndException 终止任务
/// 节流周期取自动战斗配置的 CheckTime ，与 AutoFightTask 战斗循环的检查间隔语义一致
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
        // 直接取用户自动战斗配置的战斗结束检测设置（不走 AutoFightParam，避免无关的策略路径解析等副作用）
        var c = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig;
        var detectConfig = new AutoFightParam.FightFinishDetectConfig
        {
            FastCheckEnabled = c.FastCheckEnabled,
            FastCheckParams = c.FastCheckParams,
            CheckAfterSwitchAvatar = c.CheckAfterSwitchAvatar,
            CheckEndDelay = c.CheckEndDelay,
            BeforeDetectDelay = c.BeforeDetectDelay,
            RotateFindEnemyEnabled = c.RotateFindEnemyEnabled,
            SkipFightEndCheckWhenEnemyVisible = c.SkipFightEndCheckWhenEnemyVisible,
            BlockCheckBeforeBattleSeconds = c.BlockCheckBeforeBattleSeconds,
            PaimonEndCheckEnabled = c.PaimonEndCheckEnabled,
            PaimonEndCheckDelay = c.PaimonEndCheckDelay,
        };
        _detectConfig = new AutoFightTask.TaskFightFinishDetectConfig(detectConfig);
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
