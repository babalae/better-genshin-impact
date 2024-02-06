using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;
using System.Collections.Generic;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.QuickTeleport.Assets;

public class QuickTeleportAssets
{
    public Rect MapChooseIconRoi;
    public List<RecognitionObject> MapChooseIconRoList;
    public List<Mat> MapChooseIconGreyMatList;

    public RecognitionObject TeleportButtonRo;
    public RecognitionObject MapScaleButtonRo;
    public RecognitionObject MapCloseButtonRo;
    public RecognitionObject MapChooseRo;

    public QuickTeleportAssets()
    {
        var info = TaskContext.Instance().SystemInfo;

        MapChooseIconRoi = new Rect((int)(1270 * info.AssetScale),
            (int)(100 * info.AssetScale),
            (int)(50 * info.AssetScale),
            info.CaptureAreaRect.Height - (int)(200 * info.AssetScale));
        MapChooseIconRoList = new List<RecognitionObject>
        {
            BuildMapChooseIconRo("TeleportWaypoint.png"),
            BuildMapChooseIconRo("StatueOfTheSeven.png"),
            BuildMapChooseIconRo("Domain.png"),
            BuildMapChooseIconRo("Domain2.png"),
            BuildMapChooseIconRo("PortableWaypoint.png"),
            BuildMapChooseIconRo("Mansion.png"),
            BuildMapChooseIconRo("SubSpaceWaypoint.png"),
        };
        MapChooseIconGreyMatList = MapChooseIconRoList.ConvertAll(x => x.TemplateImageGreyMat ?? new Mat());

        // 传送按钮宽泛的识别区域
        // RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 8, info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height / 8),
        TeleportButtonRo = new RecognitionObject
        {
            Name = "GoTeleport",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "GoTeleport.png"),
            RegionOfInterest = new Rect((int)(1440 * info.AssetScale),
                (int)(979 * info.AssetScale),
                (int)(60 * info.AssetScale),
                (int)(60 * info.AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        //MapScaleButtonRo = new RecognitionObject
        //{
        //    Name = "MapScaleButton",
        //    RecognitionType = RecognitionTypes.TemplateMatch,
        //    TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "ScaleWithMask.png"),
        //    RegionOfInterest = new Rect((int)(30 * info.AssetScale),
        //        (int)(440 * info.AssetScale),
        //        (int)(40 * info.AssetScale),
        //        (int)(200 * info.AssetScale)),
        //    UseMask = true,
        //    DrawOnWindow = true
        //}.InitTemplate();

        MapScaleButtonRo = new RecognitionObject
        {
            Name = "MapScaleButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapScaleButton.png"),
            RegionOfInterest = new Rect((int)(30 * info.AssetScale),
                (int)(440 * info.AssetScale),
                (int)(40 * info.AssetScale),
                (int)(200 * info.AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapCloseButtonRo = new RecognitionObject
        {
            Name = "MapCloseButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapCloseButton.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width - (int)(107 * info.AssetScale),
                (int)(19 * info.AssetScale),
                (int)(58 * info.AssetScale),
                (int)(58 * info.AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapChooseRo = new RecognitionObject
        {
            Name = "MapChoose",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapChoose.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width - (int)(480 * info.AssetScale),
                0,
                (int)(100 * info.AssetScale),
                (int)(70 * info.AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

    /// <summary>
    /// 宽泛的识别区域
    /// RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height / 3,  info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 3),
    ///
    /// 限制小区域，防止误识别
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public RecognitionObject BuildMapChooseIconRo(string name)
    {
        var info = TaskContext.Instance().SystemInfo;
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