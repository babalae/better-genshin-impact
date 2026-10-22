using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using System;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoFight.Factory;

/// <summary>
/// 自动连招（LLM 行为树）工厂 — 创建 AutoComboRunTask
/// 不对应任何策略文件：策略名为 AutoFightParam.ComboStrategyName 时路由到此，
/// 消费 AutoComboRuntime 中已构建的建树会话（尚未建树时由工厂报错终止）
/// </summary>
public class ComboCombatTaskFactory : ICombatTaskFactory
{
    /// <summary>
    /// 创建消费当前建树会话的连招行为树运行任务；尚未建树时提示用户并抛出异常
    /// </summary>
    /// <param name="runtime">战斗任务所在的运行环境</param>
    /// <param name="config">配置根</param>
    /// <returns>连招行为树运行任务</returns>
    /// <exception cref="Exception">尚未构建行为树</exception>
    public ISoloTask<AutoFightParam, NoData> CreateTask(GameRuntime runtime, AllConfig config)
    {
        var session = AutoComboRuntime.Session;
        if (session == null)
        {
            // 与 TaskRunner.Init 的用户指导型错误处理一致：弹 Toast 提示 + 抛异常中断任务
            UIDispatcherHelper.Invoke(() =>
            {
                Toast.Warning("尚未构建行为树，请先运行一次自动连招任务完成建树");
            });
            throw new Exception("尚未构建行为树，请先运行一次自动连招任务完成建树");
        }
        return new AutoComboRunTask(runtime, config, session);
    }

    public bool CanHandle(string strategyPath)
    {
        return AutoFightParam.ComboStrategyName.Equals(strategyPath);
    }
}
