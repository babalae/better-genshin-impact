using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoBoss;

/// <summary>
/// 自动首领讨伐任务参数，供独立任务、一条龙和 JS 调用统一传递配置。
/// </summary>
public class AutoBossParam : BaseTaskParam<AutoBossTask>
{
    private const string AutoStrategyName = "根据队伍自动选择";
    private string _strategyName = AutoStrategyName;
    private string _combatStrategyPath = BuildCombatStrategyPath(AutoStrategyName);
    private bool _combatStrategyPathCustomized;

    /// <summary>
    /// 需要讨伐的 Boss 名称。
    /// </summary>
    public string BossName { get; set; } = string.Empty;

    /// <summary>
    /// UI 中选择的战斗策略名称；当没有自定义策略路径时会同步更新 <see cref="CombatStrategyPath"/>。
    /// </summary>
    public string StrategyName
    {
        get => _strategyName;
        set
        {
            _strategyName = string.IsNullOrWhiteSpace(value) ? AutoStrategyName : value;
            if (!_combatStrategyPathCustomized)
            {
                _combatStrategyPath = BuildCombatStrategyPath(_strategyName);
            }
        }
    }

    /// <summary>
    /// 实际用于解析自动战斗脚本的路径。JS 可直接设置该路径来覆盖 UI 选择。
    /// </summary>
    public string CombatStrategyPath
    {
        get => _combatStrategyPath;
        set
        {
            _combatStrategyPath = value;
            _combatStrategyPathCustomized = !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>
    /// 讨伐前需要切换到的队伍名称；为空时保持当前队伍。
    /// </summary>
    public string TeamName { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用“指定讨伐次数”模式；关闭时刷取至原粹树脂耗尽。
    /// </summary>
    public bool SpecifyRunCount { get; set; }

    /// <summary>
    /// 指定模式下成功领取奖励的目标次数。
    /// </summary>
    public int RunCount { get; set; } = 1;

    /// <summary>
    /// 指定讨伐次数模式下，原粹树脂不足时是否允许使用须臾树脂补充。
    /// </summary>
    public bool UseTransientResin { get; set; }

    /// <summary>
    /// 指定讨伐次数模式下，原粹树脂不足时是否允许使用脆弱树脂补充。
    /// </summary>
    public bool UseFragileResin { get; set; }

    /// <summary>
    /// 检测到角色死亡后，回神像恢复并重试当前首领讨伐的最大次数。
    /// </summary>
    public int ReviveRetryCount { get; set; } = 3;

    /// <summary>
    /// 每轮领奖后是否先返回七天神像，再重新前往 Boss。
    /// </summary>
    public bool ReturnToStatueAfterEachRound { get; set; }

    /// <summary>
    /// 使用当前全局 AutoBoss 配置创建参数，主要用于 JS 无参构造和一条龙默认启动。
    /// </summary>
    public AutoBossParam() : base(null, null)
    {
        SetDefault();
    }

    /// <summary>
    /// 使用当前全局 AutoBoss 配置创建参数，并用传入路径覆盖实际战斗策略路径。
    /// </summary>
    /// <param name="combatStrategyPath">自动战斗策略文件或策略目录路径。</param>
    public AutoBossParam(string combatStrategyPath) : base(null, null)
    {
        SetDefault();
        CombatStrategyPath = combatStrategyPath;
    }

    /// <summary>
    /// 从当前全局 AutoBoss 配置填充默认参数。
    /// </summary>
    public void SetDefault()
    {
        SetAutoBossConfig(TaskContext.Instance().Config.AutoBossConfig);
    }

    /// <summary>
    /// 从指定 AutoBoss 配置复制可配置项，不覆盖已经通过构造参数或属性设置的自定义战斗策略路径。
    /// </summary>
    /// <param name="config">自动首领讨伐配置。</param>
    public void SetAutoBossConfig(AutoBossConfig config)
    {
        BossName = config.BossName;
        StrategyName = config.StrategyName;
        TeamName = config.TeamName;
        SpecifyRunCount = config.SpecifyRunCount;
        RunCount = config.RunCount;
        UseTransientResin = config.UseTransientResin;
        UseFragileResin = config.UseFragileResin;
        ReviveRetryCount = config.ReviveRetryCount;
        ReturnToStatueAfterEachRound = config.ReturnToStatueAfterEachRound;
    }

    /// <summary>
    /// 根据战斗策略名称重新计算实际策略路径，并清除“自定义路径”标记。
    /// </summary>
    /// <param name="strategyName">战斗策略名称；为空时使用当前全局 AutoBoss 配置的策略名称。</param>
    public void SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
        {
            strategyName = TaskContext.Instance().Config.AutoBossConfig.StrategyName;
        }

        _strategyName = string.IsNullOrWhiteSpace(strategyName) ? AutoStrategyName : strategyName;
        _combatStrategyPath = BuildCombatStrategyPath(_strategyName);
        _combatStrategyPathCustomized = false;
    }

    /// <summary>
    /// 将战斗策略名称转换为自动战斗策略文件或目录的绝对路径。
    /// </summary>
    /// <param name="strategyName">战斗策略名称。</param>
    /// <returns>策略文件路径；“根据队伍自动选择”返回自动战斗策略目录。</returns>
    private static string BuildCombatStrategyPath(string strategyName)
    {
        return AutoStrategyName.Equals(strategyName)
            ? Global.Absolute(@"User\AutoFight\")
            : Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
    }
}
