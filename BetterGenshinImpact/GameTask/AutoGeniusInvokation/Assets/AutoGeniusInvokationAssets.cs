using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;

public class AutoGeniusInvokationAssets : Singleton<AutoGeniusInvokationAssets>
{
    private readonly ISystemInfo systemInfo;

    public Mat CharacterDefeatedMat;
    public Mat CharacterStatusFreezeMat;
    public Mat CharacterStatusDizzinessMat;
    public Mat CharacterEnergyOnMat;

    public Dictionary<string, Mat> RollPhaseDiceMats;
    public Dictionary<string, Mat> ActionPhaseDiceMats;

    private AutoGeniusInvokationAssets()
    {
        systemInfo = TaskContext.Instance().SystemInfo;

        CharacterDefeatedMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色被打败.png", ImreadModes.Grayscale);

        CharacterStatusFreezeMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色状态_冻结.png", ImreadModes.Grayscale);
        CharacterStatusDizzinessMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色状态_水泡.png", ImreadModes.Grayscale);
        CharacterEnergyOnMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\满能量.png", ImreadModes.Grayscale);

        // 投掷期间的骰子
        RollPhaseDiceMats = new Dictionary<string, Mat>()
        {
            { "anemo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_anemo.png", ImreadModes.Color) },
            { "electro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_electro.png", ImreadModes.Color) },
            { "dendro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_dendro.png", ImreadModes.Color) },
            { "hydro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_hydro.png", ImreadModes.Color) },
            { "pyro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_pyro.png", ImreadModes.Color) },
            { "cryo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_cryo.png", ImreadModes.Color) },
            { "geo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_geo.png", ImreadModes.Color) },
            { "omni", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_omni.png", ImreadModes.Color) },
        };

        // 主界面骰子
        ActionPhaseDiceMats = new Dictionary<string, Mat>()
        {
            { "anemo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_anemo.png", ImreadModes.Color) },
            { "electro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_electro.png", ImreadModes.Color) },
            { "dendro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_dendro.png", ImreadModes.Color) },
            { "hydro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_hydro.png", ImreadModes.Color) },
            { "pyro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_pyro.png", ImreadModes.Color) },
            { "cryo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_cryo.png", ImreadModes.Color) },
            { "geo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_geo.png", ImreadModes.Color) },
            { "omni", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_omni.png", ImreadModes.Color) },
        };
        var msg = ActionPhaseDiceMats.Aggregate("", (current, kvp) => current + $"{kvp.Key.ToElementalType().ToChinese()}| ");
        Debug.WriteLine($"默认骰子排序：{msg}");
    }
}
