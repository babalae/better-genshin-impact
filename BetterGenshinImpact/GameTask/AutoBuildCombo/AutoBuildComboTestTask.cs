using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using CsTrees;
using CsTrees.Display;
using CsTrees.Visitors;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
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
}

/// <summary>
/// 连招行为树测试任务：循环 Tick 已构建的行为树驱动战斗，取消即暂停
/// </summary>
public class AutoBuildComboTestTask : ISoloTask
{
    public string Name => "连招行为树测试";

    public async Task Start(CancellationToken ct)
    {
        var root = AutoBuildComboRuntime.Root
            ?? throw new NormalEndException("尚未构建行为树，请先运行一次自动连招任务完成建树");

        Logger.LogInformation("{Name}任务启动，持续 Tick 行为树", Name);
        var tree = new BehaviourTree(root);
        var snapshot = new SnapshotVisitor();
        tree.AddVisitor(snapshot);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await tree.Tick();

                // 只渲染本次 Tick 遍历的路径，未访问的子树折叠为占位符
                var path = Display.AsciiTree(
                    root,
                    showOnlyVisited: true,
                    visited: snapshot.Visited,
                    previouslyVisited: snapshot.PreviouslyVisited);
                Logger.LogInformation("Tick {Count}：\n{Path}", tree.Count, path);

                // 树完成一轮评估（根节点非 Running）时稍作等待，避免空转
                if (root.Status != Status.Running)
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
            Logger.LogInformation("{Name}任务暂停，行为树状态已保留，可再次点击继续", Name);
        }
    }
}
