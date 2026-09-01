using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Assets;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using OpenCvSharp;
using System.Collections.Generic;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 茶包快速拖动传送（TpTaskFastDrag）专属资产封装。
/// 自持加载 TpTaskFastDrag 需要的、但公版 QuickTeleportAssets 缺失的 4 个按钮模板
/// （MapScaleButton / GoTeleport / MapCloseButtonWhite / MapUndergroundToGround），
/// 使 TpTaskFastDrag 不再直接引用共享 QuickTeleportAssets 的茶包扩展成员，从而自包含
/// （PR 公版时无需改动公版共享 QuickTeleportAssets）。
///
/// 解耦纪律：只"搬移"不"改逻辑"。模板加载路径（QuickTeleport 资产目录）、ROI、掩码、
/// 阈值等与茶包版 QuickTeleportAssets 逐字节一致，茶包版行为逐字节不变。
/// MapChooseIcon 相关成员（Roi / GreyMatList）为公版 QuickTeleportAssets 也存在的
/// 基础成员，此处继续转发以复用其按分辨率缓存，不重复加载。
/// </summary>
public sealed class TpTaskFastDragAssets
{
    /// <summary>按捕获分辨率缓存。模板文件属于 QuickTeleport 资产目录，与共享资产同源。</summary>
    private static readonly CaptureAssetsCache<TpTaskFastDragAssets> Cache =
        new(static size => new TpTaskFastDragAssets(size));

    private readonly QuickTeleportAssets _publicAssets;

    private TpTaskFastDragAssets(CaptureSize captureSize)
    {
        // 复用公版 QuickTeleportAssets 的分辨率资产实例（取 MapChooseIcon 成员，公版也有）。
        _publicAssets = QuickTeleportAssets.Get(captureSize.Width, captureSize.Height);

        Rect captureRect = captureSize.CaptureRect;
        double scale = captureSize.AssetScale;

        // —— 茶包自持的 4 个按钮模板（配置与茶包版 QuickTeleportAssets 逐字节一致） ——
        MapScaleButtonRo = new RecognitionObject
        {
            Name = "MapScaleButtonFastDrag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapScaleButtonFastDrag.png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect((int)(30 * scale),
                (int)(440 * scale),
                (int)(40 * scale),
                (int)(200 * scale)),
            UseMask = true,
            MaskColor = Color.FromArgb(0, 255, 0),
            DrawOnWindow = true,
            Use3Channels = true,
            TemplateMatchMode = TemplateMatchModes.SqDiffNormed,
            Threshold = 0.95
        }.InitTemplate();

        TeleportButtonRo = new RecognitionObject
        {
            Name = "GoTeleport",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "GoTeleport.png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect((int)(1440 * scale),
                captureRect.Height - (int)(120 * scale),
                (int)(100 * scale),
                (int)(120 * scale)),
            DrawOnWindow = false
        }.InitTemplate();

        MapCloseButtonWhiteRo = new RecognitionObject
        {
            Name = "MapCloseButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapCloseButtonWhite.png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect(captureRect.Width - (int)(80 * scale),
                (int)(5 * scale),
                (int)(70 * scale),
                (int)(70 * scale)),
            DrawOnWindow = true
        }.InitTemplate();

        MapUndergroundToGroundButtonRo = new RecognitionObject
        {
            Name = "MapUndergroundToGroundButtonFastDrag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapUndergroundToGroundButtonFastDrag.png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect(captureRect.Width - (int)(120 * scale),
                (int)(250 * scale),
                (int)(90 * scale),
                (int)(570 * scale)),
            UseMask = true,
            Use3Channels = true,
            MaskColor = Color.FromArgb(0, 255, 0),
            DrawOnWindow = true,
            Threshold = 0.85
        }.InitTemplate();

        // 地下图层检测按钮（方案 A）：快速传送用自己改名的 FastDrag 资源 + 茶包参数检测"当前是否在地下"，
        // 不依赖共享公版 MapUndergroundSwitchButton（恢复公版后快速传送探测失效）。
        // 参数与茶包版 QuickTeleportAssets.MapUndergroundSwitchButtonRo 一致（Use3Channels+UseMask+绿掩码+Threshold=0.8）。
        MapUndergroundSwitchButtonRo = new RecognitionObject
        {
            Name = "MapUndergroundSwitchButtonFastDrag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapUndergroundSwitchButtonFastDrag.png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect(captureRect.Width - (int)(120 * scale),
                (int)(250 * scale),
                (int)(90 * scale),
                (int)(570 * scale)),
            Use3Channels = true,
            UseMask = true,
            MaskColor = Color.FromArgb(0, 255, 0),
            DrawOnWindow = true,
            Threshold = 0.8
        }.InitTemplate();
    }

    /// <summary>传送按钮（"快速传送"触发按钮）。茶包自持加载。</summary>
    public RecognitionObject TeleportButtonRo { get; }

    /// <summary>大地图缩放滑轨按钮。茶包自持加载。</summary>
    public RecognitionObject MapScaleButtonRo { get; }

    /// <summary>弹出层（含地区菜单）的白色 X 关闭按钮。茶包自持加载。</summary>
    public RecognitionObject MapCloseButtonWhiteRo { get; }

    /// <summary>地下切回地上图层按钮。茶包自持加载。</summary>
    public RecognitionObject MapUndergroundToGroundButtonRo { get; }

    /// <summary>大地图地下图层开关（检测是否在地下）。茶包自持加载，避免共享公版 MapUndergroundSwitchButton（teleport-dual-engine-asset-separation spec / 方案 A）。</summary>
    public RecognitionObject MapUndergroundSwitchButtonRo { get; }

    /// <summary>传送点选择图标的 ROI 区域。复用自 <see cref="QuickTeleportAssets.MapChooseIconRoi"/>（公版也有）。</summary>
    public Rect MapChooseIconRoi => _publicAssets.MapChooseIconRoi;

    /// <summary>传送点选择图标灰度图列表。复用自 <see cref="QuickTeleportAssets.MapChooseIconGreyMatList"/>（公版也有）。</summary>
    public IReadOnlyList<Mat> MapChooseIconGreyMatList => _publicAssets.MapChooseIconGreyMatList;

    /// <summary>按捕获区获取资产。</summary>
    public static TpTaskFastDragAssets Get(GameTask.Model.Area.Region region) => Cache.Get(region);

    /// <summary>按捕获宽高获取资产。</summary>
    public static TpTaskFastDragAssets Get(int captureWidth, int captureHeight) => Cache.Get(captureWidth, captureHeight);
}