using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;






public class AutoFightParam : BaseTaskParam
{
    public  class FightFinishDetectConfig 
    {
        public string BattleEndProgressBarColor { get; set; }= "";

        public string BattleEndProgressBarColorTolerance { get; set; }= "";
        public bool FastCheckEnabled = false;
        public string FastCheckParams = "";
        public string CheckEndDelay = "";
        public string BeforeDetectDelay = "";
    }
    
    public AutoFightParam(string path, AutoFightConfig autoFightConfig)
    {
        CombatStrategyPath = path;
        Timeout = autoFightConfig.Timeout;
        FightFinishDetectEnabled = autoFightConfig.FightFinishDetectEnabled;
        PickDropsAfterFightEnabled = autoFightConfig.PickDropsAfterFightEnabled;
        PickDropsAfterFightSeconds = autoFightConfig.PickDropsAfterFightSeconds;
        KazuhaPickupEnabled = autoFightConfig.KazuhaPickupEnabled;
        ActionSchedulerByCd = autoFightConfig.ActionSchedulerByCd;
       
        FinishDetectConfig.FastCheckEnabled = autoFightConfig.FinishDetectConfig.FastCheckEnabled;
        FinishDetectConfig.FastCheckParams = autoFightConfig.FinishDetectConfig.FastCheckParams;
        FinishDetectConfig.CheckEndDelay = autoFightConfig.FinishDetectConfig.CheckEndDelay;
        FinishDetectConfig.BeforeDetectDelay = autoFightConfig.FinishDetectConfig.BeforeDetectDelay;
        KazuhaPartyName = autoFightConfig.KazuhaPartyName;
        OnlyPickEliteDropsMode = autoFightConfig.OnlyPickEliteDropsMode;
        //下面参数固定，只取自动战斗里面的
        FinishDetectConfig.BattleEndProgressBarColor = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.BattleEndProgressBarColor;
        FinishDetectConfig.BattleEndProgressBarColorTolerance = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.BattleEndProgressBarColorTolerance;
    }

    public FightFinishDetectConfig FinishDetectConfig { get; set; } = new();

    public string CombatStrategyPath { get; set; }

    public bool FightFinishDetectEnabled { get; set; } = false;
    public bool PickDropsAfterFightEnabled { get; set; } = false;
    public int PickDropsAfterFightSeconds { get; set; } = 15;

    public int Timeout { get; set; } = 120;

    public bool KazuhaPickupEnabled = true;
    public string ActionSchedulerByCd = "";
    public string KazuhaPartyName;
    public string OnlyPickEliteDropsMode="";


}