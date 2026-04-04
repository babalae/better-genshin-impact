using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 负责处理自动战斗（AutoFight）动作的执行逻辑。
/// </summary>
/// <remarks>
/// 主要用于在自动寻路（AutoPathing）过程中，到达特定触发点时执行战斗任务，
/// 支持根据队伍配置、怪物类型（普通、精英、传奇）以及掉落物（Loot）拾取策略进行动态干预。
/// </remarks>
public class AutoFightHandler : IActionHandler
{
    private readonly ILogger<AutoFightHandler> _logger = App.GetLogger<AutoFightHandler>();

    /// <inheritdoc/>
    public async Task RunAsyncByScript(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null) => await StartFightAsync(ct, config, waypointForTrack);

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await StartFightAsync(ct, config, waypointForTrack);
    }

    /// <summary>
    /// 开始执行自动战斗任务的核心流程。
    /// </summary>
    /// <param name="ct">异步操作取消令牌（CancellationToken）。</param>
    /// <param name="config">当前队伍的战斗策略配置对象，通常应为 <see cref="PathingPartyConfig"/> 类型。</param>
    /// <param name="waypointForTrack">触发战斗的追踪航点（Waypoint）信息，包含怪物标签（MonsterTag）及掉落物配置。</param>
    /// <returns>代表战斗任务执行过程的 <see cref="Task"/>。</returns>
    private async Task StartFightAsync(CancellationToken ct, object? config = null, WaypointForTrack? waypointForTrack = null)
    {
        TaskControl.Logger.LogInformation("执行自动战斗任务");
        
        AutoFightParam taskParams;
        if (config is PathingPartyConfig partyConfig && partyConfig.AutoFightEnabled)
        {
            taskParams = CreateFightParam(partyConfig.AutoFightConfig);
        }
        else
        {
            var fallbackConfig = TaskContext.Instance().Config.AutoFightConfig;
            taskParams = new AutoFightParam(GetStrategyPath(fallbackConfig), fallbackConfig);
        }

        ProcessMonsterLootConfiguration(waypointForTrack, taskParams);

        if (waypointForTrack != null && waypointForTrack.EnableMonsterLootSplit && 
           !(waypointForTrack.MonsterTag == "elite" || waypointForTrack.MonsterTag == "legendary") &&
            taskParams.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite")
        {
            await RunnerContext.Instance.StopAutoPickRunTask(
                async () => await new AutoFightTask(taskParams).Start(ct),
                5);
            return;
        }

        var fightSoloTask = new AutoFightTask(taskParams);
        await fightSoloTask.Start(ct);
    }

    /// <summary>
    /// 处理怪物掉落物拾取配置，根据航点怪物类型（MonsterTag/精英/传奇等）动态调整战斗拾取参数。
    /// </summary>
    /// <param name="waypointForTrack">当前追踪的航点信息。</param>
    /// <param name="taskParams">需应用规则的自动战斗参数配置 <see cref="AutoFightParam"/>。</param>
    /// <remarks>
    /// 若处于非精英怪点且启用了相关策略，则可安全关闭万叶聚怪拾取及战后统一拾取。
    /// </remarks>
    private void ProcessMonsterLootConfiguration(WaypointForTrack? waypointForTrack, AutoFightParam taskParams)
    {
        if (waypointForTrack?.EnableMonsterLootSplit != true)
        {
            return;
        }

        // normal, elite, legendary
        if (waypointForTrack.MonsterTag == "elite" || waypointForTrack.MonsterTag == "legendary")
        {
            return;
        }

        if (taskParams.OnlyPickEliteDropsMode is "AllowAutoPickupForNonElite" or "DisableAutoPickupForNonElite")
        {
            taskParams.KazuhaPickupEnabled = false;
            taskParams.PickDropsAfterFightEnabled = false;
            _logger.LogInformation("当前点位非精英/传奇，已关闭战斗关联的拾取配置");
        }
    }

    /// <summary>
    /// 根据自动战斗预设配置实例化执行期参数。
    /// </summary>
    /// <param name="config">选定的自动战斗配置对象 <see cref="AutoFightConfig"/>。</param>
    /// <returns>实例化完毕并包含策略路径的 <see cref="AutoFightParam"/> 参数对象。</returns>
    private AutoFightParam CreateFightParam(AutoFightConfig? config)
    {
        return new AutoFightParam(GetStrategyPath(config), config);
    }

    /// <summary>
    /// 获取当前生效战斗策略（Strategy）文本文档在磁盘上的绝对路径。
    /// </summary>
    /// <param name="config">提供所选策略名 <see cref="AutoFightConfig.StrategyName"/> 的配置对象。</param>
    /// <returns>指向策略目录或策略文件的完整绝对路径（User/AutoFight/...）。</returns>
    /// <exception cref="ArgumentNullException">当传入的 <paramref name="config"/> 为 <c>null</c> 时引发。</exception>
    /// <exception cref="FileNotFoundException">当对应的策略路径（文件或目录）不存在时引发。</exception>
    private string GetStrategyPath(AutoFightConfig? config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        var strategyName = config.StrategyName;
        var path = "根据队伍自动选择".Equals(strategyName, StringComparison.OrdinalIgnoreCase) 
            ? Global.Absolute(@"User\AutoFight\")
            : Global.Absolute($@"User\AutoFight\{strategyName}.txt");

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"战斗策略文件不存在 {path}");
        }

        return path;
    }
}

