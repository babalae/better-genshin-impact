using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets
{
    public class AutoGeniusInvokationAssets
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

        public AutoGeniusInvokationAssets()
        {
            var info = TaskContext.Instance().SystemInfo;
            ConfirmButtonRo = new RecognitionObject
            {
                Name = "ConfirmButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\确定.png"),
                DrawOnWindow = true
            }.InitTemplate();
            RoundEndButtonRo = new RecognitionObject
            {
                Name = "RoundEndButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\回合结束.png"),
                RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 5, info.CaptureAreaRect.Height),
                DrawOnWindow = true
            }.InitTemplate();
            ElementalTuningConfirmButtonRo = new RecognitionObject
            {
                Name = "ElementalTuningConfirmButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\元素调和.png"),
                RegionOfInterest = new Rect(0, info.CaptureAreaRect.Height/2, info.CaptureAreaRect.Width, info.CaptureAreaRect.Height/2),
                Threshold = 0.9,
                DrawOnWindow = true
            }.InitTemplate();
            ExitDuelButtonRo = new RecognitionObject
            {
                Name = "ExitDuelButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\退出挑战.png"),
                RegionOfInterest = new Rect(0, info.CaptureAreaRect.Height / 2, info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 2),
                DrawOnWindow = true
            }.InitTemplate();
            InOpponentActionRo = new RecognitionObject
            {
                Name = "InOpponentAction",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\对方行动中.png"),
                RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 5, info.CaptureAreaRect.Height),
                DrawOnWindow = true
            }.InitTemplate();
            EndPhaseRo = new RecognitionObject
            {
                Name = "EndPhase",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\回合结算阶段.png"),
                RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 5, info.CaptureAreaRect.Height),
                DrawOnWindow = true
            }.InitTemplate();
            ElementalDiceLackWarningRo = new RecognitionObject
            {
                Name = "ElementalDiceLackWarning",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\元素骰子不足.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2, 0,
                    info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height),
                DrawOnWindow = true
            }.InitTemplate();
            CharacterTakenOutRo = new RecognitionObject
            {
                Name = "CharacterTakenOut",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\角色死亡.png"),
                DrawOnWindow = true
            }.InitTemplate();


            CharacterDefeatedMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\角色被打败.png", ImreadModes.Grayscale);

            InCharacterPickRo = new RecognitionObject
            {
                Name = "InCharacterPick",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\出战角色.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height / 2, 
                    info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 2),
                DrawOnWindow = true
            }.InitTemplate();
            CharacterHpUpperRo = new RecognitionObject
            {
                Name = "CharacterHpUpper",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\角色血量上方.png"),
                DrawOnWindow = true
            }.InitTemplate();


            CharacterStatusFreezeMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\角色状态_冻结.png", ImreadModes.Grayscale);
            CharacterStatusDizzinessMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\角色状态_水泡.png", ImreadModes.Grayscale);
            CharacterEnergyOnMat = GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"other\满能量.png", ImreadModes.Grayscale);

            // 投掷期间的骰子
            RollPhaseDiceMats = new Dictionary<string, Mat>()
            {
                { "anemo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_anemo.png", ImreadModes.Grayscale) },
                { "electro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_electro.png", ImreadModes.Grayscale) },
                { "dendro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_dendro.png", ImreadModes.Grayscale) },
                { "hydro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_hydro.png", ImreadModes.Grayscale) },
                { "pyro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_pyro.png", ImreadModes.Grayscale) },
                { "cryo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_cryo.png", ImreadModes.Grayscale) },
                { "geo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_geo.png", ImreadModes.Grayscale) },
                { "omni", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\roll_omni.png", ImreadModes.Grayscale) },
            };

            // 主界面骰子
            ActionPhaseDiceMats = new Dictionary<string, Mat>()
            {
                { "anemo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_anemo.png", ImreadModes.Grayscale) },
                { "electro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_electro.png", ImreadModes.Grayscale) },
                { "dendro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_dendro.png", ImreadModes.Grayscale) },
                { "hydro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_hydro.png", ImreadModes.Grayscale) },
                { "pyro", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_pyro.png", ImreadModes.Grayscale) },
                { "cryo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_cryo.png", ImreadModes.Grayscale) },
                { "geo", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_geo.png", ImreadModes.Grayscale) },
                { "omni", GameTaskManager.LoadAssertImage("AutoGeniusInvokation", @"dice\action_omni.png", ImreadModes.Grayscale) },
            };
        }
    }
}