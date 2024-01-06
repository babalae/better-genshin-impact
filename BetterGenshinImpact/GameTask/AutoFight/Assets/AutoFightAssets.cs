using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public class AutoFightAssets
{
    public Rect TeamRect;
    public List<Rect> AvatarIndexRectList;
    public Rect ERect;
    public Rect QRect;
    public Rect EndTipsRect;
    public RecognitionObject WandererIconRa;
    public RecognitionObject ConfirmRa;
    public RecognitionObject ExitRa;
    public RecognitionObject ClickAnyCloseTipRa;
    public RecognitionObject UseCondensedResinRa;

    // 树脂状态
    public RecognitionObject CondensedResinCountRa;
    public RecognitionObject FragileResinCountRa;

    public AutoFightAssets()
    {
        var info = TaskContext.Instance().SystemInfo;
        var captureRect = info.CaptureAreaRect;
        var assetScale = info.AssetScale;

        TeamRect = new Rect(captureRect.Width - (int)(355 * assetScale), (int)(220 * assetScale),
            (int)(355 * assetScale), (int)(465 * assetScale));
        ERect = new Rect(captureRect.Width - (int)(267 * assetScale), captureRect.Height - (int)(132 * assetScale),
            (int)(77 * assetScale), (int)(77 * assetScale));
        QRect = new Rect(captureRect.Width - (int)(157 * assetScale), captureRect.Height - (int)(165 * assetScale),
            (int)(110 * assetScale), (int)(110 * assetScale));
        // 结束提示从中间开始找相对位置
        EndTipsRect = new Rect(captureRect.Width / 2 - (int)(200 * assetScale), captureRect.Height - (int)(160 * assetScale),
            (int)(400 * assetScale), (int)(80 * assetScale));


        AvatarIndexRectList = new List<Rect>
        {
            new(captureRect.Width - (int)(61 * assetScale), (int)(256 * assetScale), (int)(28 * assetScale), (int)(24 * assetScale)),
            new(captureRect.Width - (int)(61 * assetScale), (int)(352 * assetScale), (int)(28 * assetScale), (int)(24 * assetScale)),
            new(captureRect.Width - (int)(61 * assetScale), (int)(448 * assetScale), (int)(28 * assetScale), (int)(24 * assetScale)),
            new(captureRect.Width - (int)(61 * assetScale), (int)(544 * assetScale), (int)(28 * assetScale), (int)(24 * assetScale)),
        };

        WandererIconRa = new RecognitionObject
        {
            Name = "WandererIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "wanderer_icon.png"),
            DrawOnWindow = false
        }.InitTemplate();

        // 右下角的按钮
        ConfirmRa = new RecognitionObject
        {
            Name = "Confirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "confirm.png"),
            RegionOfInterest = new Rect(captureRect.Width / 2, captureRect.Height / 2, captureRect.Width / 2, captureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        // 点击任意处关闭提示
        ClickAnyCloseTipRa = new RecognitionObject
        {
            Name = "ClickAnyCloseTip",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "click_any_close_tip.png"),
            RegionOfInterest = new Rect(0, captureRect.Height / 2, captureRect.Width, captureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        UseCondensedResinRa = new RecognitionObject
        {
            Name = "UseCondensedResin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "use_condensed_resin.png"),
            RegionOfInterest = new Rect(0, captureRect.Height / 2, captureRect.Width / 2, captureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        ExitRa = new RecognitionObject
        {
            Name = "Exit",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "exit.png"),
            RegionOfInterest = new Rect(0, captureRect.Height / 2, captureRect.Width / 2, captureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        CondensedResinCountRa = new RecognitionObject
        {
            Name = "CondensedResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "condensed_resin_count.png"),
            RegionOfInterest = new Rect(captureRect.Width / 2, captureRect.Height / 3 * 2, captureRect.Width / 2, captureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();
        FragileResinCountRa = new RecognitionObject
        {
            Name = "FragileResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "fragile_resin_count.png"),
            RegionOfInterest = new Rect(captureRect.Width / 2, captureRect.Height / 3 * 2, captureRect.Width / 2, captureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();
    }
}