using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.QuickTeleport.Assets;

public class QuickTeleportAssets : BaseAssets<QuickTeleportAssets>
{
    public Rect MapChooseIconRoi;
    public List<RecognitionObject> MapChooseIconRoList;
    public List<Mat> MapChooseIconGreyMatList;

    public RecognitionObject TeleportButtonRo;
    public RecognitionObject MapScaleButtonRo;
    public RecognitionObject MapCloseButtonRo;
    public RecognitionObject MapSettingsButtonRo;
    public RecognitionObject MapChooseRo;

    public RecognitionObject MapUndergroundSwitchButtonRo;
    public RecognitionObject MapUndergroundToGroundButtonRo;

    private QuickTeleportAssets()
    {
        MapChooseIconRoi = new Rect((int)(1270 * AssetScale),
            (int)(100 * AssetScale),
            (int)(50 * AssetScale),
            CaptureRect.Height - (int)(200 * AssetScale));
        MapChooseIconRoList =
        [
            BuildMapChooseIconRo("TeleportWaypoint.png"),
            BuildMapChooseIconRo("StatueOfTheSeven.png"),
            BuildMapChooseIconRo("Domain.png"),
            BuildMapChooseIconRo("Domain2.png"),
            BuildMapChooseIconRo("ObsidianTotemPole.png"),
            BuildMapChooseIconRo("PortableWaypoint.png"),
            BuildMapChooseIconRo("Mansion.png"),
            BuildMapChooseIconRo("SubSpaceWaypoint.png"),
            BuildMapChooseIconRo("NodKraiMeetingPoint.png"),
            BuildMapChooseIconRo("TabletOfTona.png"),
        ];
        MapChooseIconGreyMatList = MapChooseIconRoList.ConvertAll(x => x.TemplateImageGreyMat ?? new Mat());

        // 传送按钮宽泛的识别区域
        // RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 4, CaptureRect.Height - CaptureRect.Height / 8, CaptureRect.Width / 4, CaptureRect.Height / 8),
        TeleportButtonRo = new RecognitionObject
        {
            Name = "GoTeleport",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "GoTeleport.png"),
            RegionOfInterest = new Rect((int)(1440 * AssetScale),
                CaptureRect.Height - (int)(120 * AssetScale),
                (int)(100 * AssetScale),
                (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        //MapScaleButtonRo = new RecognitionObject
        //{
        //    Name = "MapScaleButton",
        //    RecognitionType = RecognitionTypes.TemplateMatch,
        //    TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "ScaleWithMask.png"),
        //    RegionOfInterest = new Rect((int)(30 * AssetScale),
        //        (int)(440 * AssetScale),
        //        (int)(40 * AssetScale),
        //        (int)(200 * AssetScale)),
        //    UseMask = true,
        //    DrawOnWindow = true
        //}.InitTemplate();

        MapScaleButtonRo = new RecognitionObject
        {
            Name = "MapScaleButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapScaleButton.png"),
            RegionOfInterest = new Rect((int)(30 * AssetScale),
                (int)(440 * AssetScale),
                (int)(40 * AssetScale),
                (int)(200 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapCloseButtonRo = new RecognitionObject
        {
            Name = "MapCloseButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapCloseButton.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(107 * AssetScale),
                (int)(19 * AssetScale),
                (int)(58 * AssetScale),
                (int)(58 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapSettingsButtonRo = new RecognitionObject
        {
            Name = "MapSettingsButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapSettingsButton.png"),
            RegionOfInterest = new Rect((int)(25 * AssetScale),
                (int)(990 * AssetScale),
                (int)(58 * AssetScale),
                (int)(62 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapChooseRo = new RecognitionObject
        {
            Name = "MapChoose",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapChoose.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(480 * AssetScale),
                0,
                (int)(100 * AssetScale),
                (int)(70 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 地下切换按钮
        // 由于有颜色相近的内容，所以使用3通道
        MapUndergroundSwitchButtonRo = new RecognitionObject
        {
            Name = "MapUndergroundSwitchButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            Use3Channels = true,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapUndergroundSwitchButton.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(120 * AssetScale),
                (int)(250 * AssetScale),
                (int)(90 * AssetScale),
                (int)(570 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        MapUndergroundToGroundButtonRo = new RecognitionObject
        {
            Name = "MapUndergroundToGroundButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapUndergroundToGroundButton.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(120 * AssetScale),
                (int)(250 * AssetScale),
                (int)(90 * AssetScale),
                (int)(570 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

    /// <summary>
    /// 宽泛的识别区域
    /// RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 3,  CaptureRect.Width / 4, CaptureRect.Height - CaptureRect.Height / 3),
    ///
    /// 限制小区域，防止误识别
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public RecognitionObject BuildMapChooseIconRo(string name)
    {
        var ro = new RecognitionObject
        {
            Name = name + "MapChooseIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", name),
            RegionOfInterest = MapChooseIconRoi,
            DrawOnWindow = false
        }.InitTemplate();

        if (name == "TeleportWaypoint.png")
        {
            ro.Threshold = 0.7;
        }

        return ro;
    }
}
