using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;

public class AutoGeniusInvokationAssets : BaseAssets<AutoGeniusInvokationAssets>
{
    public RecognitionObject ConfirmButtonRo;
    public RecognitionObject RoundEndButtonRo;
    public RecognitionObject ElementalTuningConfirmButtonRo;
    public RecognitionObject ExitDuelButtonRo;

    public RecognitionObject InOpponentActionRo;
    public RecognitionObject EndPhaseRo;
    public RecognitionObject ElementalDiceLackWarningRo;
    public RecognitionObject CharacterTakenOutRo;
    public Mat CharacterDefeatedMat;
    public RecognitionObject InCharacterPickRo;

    // 角色区域
    public RecognitionObject CharacterHpUpperRo;

    public Mat CharacterStatusFreezeMat;
    public Mat CharacterStatusDizzinessMat;
    public Mat CharacterEnergyOnMat;

    public Dictionary<string, Mat> RollPhaseDiceMats;
    public Dictionary<string, Mat> ActionPhaseDiceMats;

    private AutoGeniusInvokationAssets()
    {
        ConfirmButtonRo = new RecognitionObject
        {
            Name = "ConfirmButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\确定.png"),
            DrawOnWindow = false
        }.InitTemplate();
        RoundEndButtonRo = new RecognitionObject
        {
            Name = "RoundEndButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\回合结束.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 5, CaptureRect.Height),
            DrawOnWindow = true
        }.InitTemplate();
        ElementalTuningConfirmButtonRo = new RecognitionObject
        {
            Name = "ElementalTuningConfirmButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\元素调和.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height / 2),
            Threshold = 0.9,
            DrawOnWindow = false
        }.InitTemplate();
        ExitDuelButtonRo = new RecognitionObject
        {
            Name = "ExitDuelButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\退出挑战.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = true
        }.InitTemplate();
        InOpponentActionRo = new RecognitionObject
        {
            Name = "InOpponentAction",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\对方行动中.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 5, CaptureRect.Height),
            DrawOnWindow = true
        }.InitTemplate();
        EndPhaseRo = new RecognitionObject
        {
            Name = "EndPhase",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\回合结算阶段.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 5, CaptureRect.Height),
            DrawOnWindow = true
        }.InitTemplate();
        ElementalDiceLackWarningRo = new RecognitionObject
        {
            Name = "ElementalDiceLackWarning",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\元素骰子不足.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 2, 0,
                CaptureRect.Width / 2, CaptureRect.Height),
            DrawOnWindow = true
        }.InitTemplate();
        CharacterTakenOutRo = new RecognitionObject
        {
            Name = "CharacterTakenOut",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色死亡.png"),
            DrawOnWindow = true
        }.InitTemplate();

        CharacterDefeatedMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色被打败.png", ImreadModes.Grayscale);

        InCharacterPickRo = new RecognitionObject
        {
            Name = "InCharacterPick",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\出战角色.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2,
                CaptureRect.Width - CaptureRect.Width / 2,
                CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = true
        }.InitTemplate();
        CharacterHpUpperRo = new RecognitionObject
        {
            Name = "CharacterHpUpper",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoGeniusInvokation", @"other\角色血量上方.png"),
            DrawOnWindow = true
        }.InitTemplate();

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
