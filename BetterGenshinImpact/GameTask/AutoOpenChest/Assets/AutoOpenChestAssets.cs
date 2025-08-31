using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing.Assets;

public class AutoOpenChestAssets : BaseAssets<AutoOpenChestAssets>
{
    public RecognitionObject ChestIconRo;
    public RecognitionObject ChestFIconRo;
    public RecognitionObject FlowerFIconRo;


#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private AutoOpenChestAssets() : base()
    {
        Initialization(this.systemInfo);
    }

    protected AutoOpenChestAssets(ISystemInfo systemInfo) : base(systemInfo)
    {
        Initialization(systemInfo);
    }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

    private void Initialization(ISystemInfo systemInfo)
    {
        var assetScale = systemInfo.AssetScale;
        ChestIconRo = new RecognitionObject
        {
            Name = "ChestIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoOpenChest", "chest.png", systemInfo),
            RegionOfInterest = new Rect((int)(330* assetScale),
            (int)(130*assetScale),
            (int)(1250*assetScale),
            (int)(840 * assetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        ChestFIconRo = new RecognitionObject
        {
            Name = "ChestFIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoOpenChest", "chest_F_icon.png", systemInfo),
            RegionOfInterest = new Rect((int)(1150 * assetScale),(int)(450 * assetScale),(int)(100 * assetScale),(int)(300 * assetScale)),
            DrawOnWindow = false
        }.InitTemplate();


        FlowerFIconRo = new RecognitionObject
        {
            Name = "FlowerFIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoOpenChest", "flower_F_icon.png", systemInfo),
            RegionOfInterest = new Rect((int)(1150 * assetScale), (int)(450 * assetScale), (int)(100 * assetScale), (int)(300 * assetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }
}
