using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.QuickTeleport.Assets;

public class QuickTeleportAssets : Singleton<QuickTeleportAssets>
{
    private readonly ISystemInfo systemInfo;

    public Rect MapChooseIconRoi;
    public List<RecognitionObject> MapChooseIconRoList;
    public List<Mat> MapChooseIconGreyMatList;

    private Rect CaptureRect => systemInfo.ScaleMax1080PCaptureRect;
    private double AssetScale => systemInfo.AssetScale;

    private QuickTeleportAssets()
    {
        systemInfo = TaskContext.Instance().SystemInfo;

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
