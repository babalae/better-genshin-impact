using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Model.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;

public sealed class AutoGeniusInvokationAssets
{
    private static readonly CaptureAssetsCache<AutoGeniusInvokationAssets> Cache = new(
        static size => new AutoGeniusInvokationAssets(size));

    public Mat CharacterDefeatedMat { get; }
    public Mat CharacterStatusFreezeMat { get; }
    public Mat CharacterStatusDizzinessMat { get; }
    public Mat CharacterEnergyOnMat { get; }

    public IReadOnlyDictionary<string, Mat> RollPhaseDiceMats { get; }
    public IReadOnlyDictionary<string, Mat> ActionPhaseDiceMats { get; }

    private AutoGeniusInvokationAssets(CaptureSize captureSize)
    {
        var captureWidth = captureSize.Width;
        var captureHeight = captureSize.Height;
        CharacterDefeatedMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色被打败.png", captureWidth, captureHeight, ImreadModes.Grayscale);

        CharacterStatusFreezeMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色状态_冻结.png", captureWidth, captureHeight, ImreadModes.Grayscale);
        CharacterStatusDizzinessMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色状态_水泡.png", captureWidth, captureHeight, ImreadModes.Grayscale);
        CharacterEnergyOnMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\满能量.png", captureWidth, captureHeight, ImreadModes.Grayscale);

        // 投掷期间的骰子
        RollPhaseDiceMats = new Dictionary<string, Mat>()
        {
            { "anemo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_anemo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "electro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_electro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "dendro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_dendro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "hydro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_hydro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "pyro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_pyro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "cryo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_cryo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "geo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_geo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "omni", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\roll_omni.png", captureWidth, captureHeight, ImreadModes.Color) },
        };

        // 主界面骰子
        ActionPhaseDiceMats = new Dictionary<string, Mat>()
        {
            { "anemo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_anemo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "electro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_electro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "dendro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_dendro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "hydro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_hydro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "pyro", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_pyro.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "cryo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_cryo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "geo", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_geo.png", captureWidth, captureHeight, ImreadModes.Color) },
            { "omni", GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"dice\action_omni.png", captureWidth, captureHeight, ImreadModes.Color) },
        };
        var msg = ActionPhaseDiceMats.Aggregate("", (current, kvp) => current + $"{kvp.Key.ToElementalType().ToChinese()}| ");
        Debug.WriteLine($"默认骰子排序：{msg}");
    }

    public static AutoGeniusInvokationAssets Get(Region region)
    {
        return Cache.Get(region);
    }

    public static AutoGeniusInvokationAssets Get(int captureWidth, int captureHeight)
    {
        return Cache.Get(captureWidth, captureHeight);
    }
}
