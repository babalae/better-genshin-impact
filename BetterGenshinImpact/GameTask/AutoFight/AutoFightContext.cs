using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗上下文
/// 请在启动BetterGI以后再初始化
/// </summary>
public class AutoFightContext : Singleton<AutoFightContext>
{
    private AutoFightContext()
    {
        Simulator = Simulation.PostMessage(TaskContext.Instance().GameHandle);
    }

    /// <summary>
    /// find资源
    /// </summary>
    public AutoFightAssets FightAssets => AutoFightAssets.Instance;

    /// <summary>
    /// 战斗专用的PostMessage模拟键鼠操作
    /// </summary>
    public readonly PostMessageSimulator Simulator;
}
