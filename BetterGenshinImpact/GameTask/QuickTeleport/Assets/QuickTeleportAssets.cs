using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.QuickTeleport.Assets;

public sealed class QuickTeleportAssets
{
    private static readonly CaptureAssetsCache<QuickTeleportAssets> Cache = new(static size => new QuickTeleportAssets(size));

    public Rect MapChooseIconRoi { get; }
    public IReadOnlyList<RecognitionObject> MapChooseIconRoList { get; }
    public IReadOnlyList<Mat> MapChooseIconGreyMatList { get; }

    private QuickTeleportAssets(CaptureSize captureSize)
    {
        MapChooseIconRoi = new Rect((int)(1270 * captureSize.AssetScale),
            (int)(100 * captureSize.AssetScale),
            (int)(50 * captureSize.AssetScale),
            captureSize.Height - (int)(200 * captureSize.AssetScale));
        List<RecognitionObject> mapChooseIconRoList =
        [
            BuildMapChooseIconRo("TeleportWaypoint.png", captureSize),
            BuildMapChooseIconRo("StatueOfTheSeven.png", captureSize),
            BuildMapChooseIconRo("Domain.png", captureSize),
            BuildMapChooseIconRo("Domain2.png", captureSize),
            BuildMapChooseIconRo("ObsidianTotemPole.png", captureSize),
            BuildMapChooseIconRo("PortableWaypoint.png", captureSize),
            BuildMapChooseIconRo("Mansion.png", captureSize),
            BuildMapChooseIconRo("SubSpaceWaypoint.png", captureSize),
            BuildMapChooseIconRo("NodKraiMeetingPoint.png", captureSize),
            BuildMapChooseIconRo("TabletOfTona.png", captureSize),
        ];
        MapChooseIconRoList = mapChooseIconRoList.AsReadOnly();
        MapChooseIconGreyMatList = mapChooseIconRoList.ConvertAll(x => x.TemplateImageGreyMat ?? new Mat()).AsReadOnly();
    }

    public static QuickTeleportAssets Get(Region region)
    {
        return Cache.Get(region);
    }

    public static QuickTeleportAssets Get(int captureWidth, int captureHeight)
    {
        return Cache.Get(captureWidth, captureHeight);
    }

    /// <summary>
    /// 宽泛的识别区域
    /// RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 3,  CaptureRect.Width / 4, CaptureRect.Height - CaptureRect.Height / 3),
    ///
    /// 限制小区域，防止误识别
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private RecognitionObject BuildMapChooseIconRo(string name, CaptureSize captureSize)
    {
        var ro = new RecognitionObject
        {
            Name = name + "MapChooseIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", name, captureSize.Width, captureSize.Height),
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
