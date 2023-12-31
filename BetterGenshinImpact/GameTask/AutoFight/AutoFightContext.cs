using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗上下文
/// 请在启动BetterGI以后再初始化
/// </summary>
public class AutoFightContext
{

    private static AutoFightContext? _uniqueInstance;
    private static readonly object Locker = new();

    private AutoFightContext()
    {
        FightAssets = new();
        Simulator = Simulation.PostMessage(TaskContext.Instance().GameHandle);
    }

    public static AutoFightContext Instance()
    {
        if (_uniqueInstance == null)
        {
            lock (Locker)
            {
                _uniqueInstance ??= new AutoFightContext();
            }
        }
        return _uniqueInstance;
    }

    /// <summary>
    /// find资源
    /// </summary>
    public readonly AutoFightAssets FightAssets;

    /// <summary>
    /// 战斗专用的PostMessage模拟键鼠操作
    /// </summary>
    public readonly PostMessageSimulator Simulator;
}