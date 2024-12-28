using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightParam : BaseTaskParam
{
    public AutoFightParam(string path, AutoFightConfig autoFightConfig)
    {
        CombatStrategyPath = path;
        Timeout = autoFightConfig.Timeout;
        FightFinishDetectEnabled = autoFightConfig.FightFinishDetectEnabled;
        PickDropsAfterFightEnabled = autoFightConfig.PickDropsAfterFightEnabled;

        //下面参数固定，只取自动战斗里面的
        autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
        BattleEndProgressBarColor = autoFightConfig.BattleEndProgressBarColor;
        BattleEndProgressBarColorTolerance = autoFightConfig.BattleEndProgressBarColorTolerance;
    }

    public AutoFightParam(string path)
    {
        CombatStrategyPath = path;
    }

    public string CombatStrategyPath { get; set; }

    public bool FightFinishDetectEnabled { get; set; } = false;

    public bool PickDropsAfterFightEnabled { get; set; } = false;

    public int Timeout { get; set; } = 120;


    public string BattleEndProgressBarColor = "";


    public string BattleEndProgressBarColorTolerance = "";
}