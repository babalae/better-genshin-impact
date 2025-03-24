using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing.Assets;

public class AutoFishingAssets : BaseAssets<AutoFishingAssets>
{
    public RecognitionObject SpaceButtonRo;

    public RecognitionObject BaitButtonRo;
    public RecognitionObject WaitBiteButtonRo;
    public RecognitionObject LiftRodButtonRo;
    public RecognitionObject ExitFishingButtonRo;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private AutoFishingAssets() : base()
    {
        Initialization(this.systemInfo);
    }

    protected AutoFishingAssets(ISystemInfo systemInfo) : base(systemInfo)
    {
        Initialization(systemInfo);
    }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

    private void Initialization(ISystemInfo systemInfo)
    {
        SpaceButtonRo = new RecognitionObject
        {
            Name = "SpaceButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", "space.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 3,
                CaptureRect.Height - CaptureRect.Height / 5,
                CaptureRect.Width / 3,
                CaptureRect.Height / 5),
            DrawOnWindow = false
        }.InitTemplate();

        BaitButtonRo = new RecognitionObject
        {
            Name = "BaitButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", "switch_bait.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 2,
                CaptureRect.Height - CaptureRect.Height / 4,
                CaptureRect.Width / 2,
                CaptureRect.Height / 4),
            Threshold = 0.7,
            DrawOnWindow = false
        }.InitTemplate();
        WaitBiteButtonRo = new RecognitionObject
        {
            Name = "WaitBiteButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", "wait_bite.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 2,
                CaptureRect.Height - CaptureRect.Height / 4,
                CaptureRect.Width / 2,
                CaptureRect.Height / 4),
            Threshold = 0.7,
            DrawOnWindow = false
        }.InitTemplate();
        LiftRodButtonRo = new RecognitionObject
        {
            Name = "LiftRodButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", "lift_rod.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 2,
                CaptureRect.Height - CaptureRect.Height / 4,
                CaptureRect.Width / 2,
                CaptureRect.Height / 4),
            Threshold = 0.7,
            DrawOnWindow = false
        }.InitTemplate();

        var w = (int)(140 * AssetScale);
        var h = (int)(150 * AssetScale);
        ExitFishingButtonRo = new RecognitionObject
        {
            Name = "ExitFishingButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", "exit_fishing.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - w,
                CaptureRect.Height - h,
                w,
                h),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
    }
}
