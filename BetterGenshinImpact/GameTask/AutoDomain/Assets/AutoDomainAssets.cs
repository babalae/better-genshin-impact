using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.Assets;

public class AutoDomainAssets : BaseAssets<AutoDomainAssets>
{
    public RecognitionObject ResinSwitchBtnRo;
    public RecognitionObject ResinSwitchBtnNoActiveRo;


    private AutoDomainAssets() : base()
    {
        Initialization(this.systemInfo);
    }

    protected AutoDomainAssets(ISystemInfo systemInfo) : base(systemInfo)
    {
        Initialization(systemInfo);
    }



    private void Initialization(ISystemInfo systemInfo)
    {
        // 可点击状态的树脂切换按钮
        ResinSwitchBtnRo = new RecognitionObject
        {
            Name = "ResinSwitchBtn",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoDomain", "resin_switch_btn.png", this.systemInfo),
            RegionOfInterest = new Rect((int)(960 * AssetScale), (int)(430 * AssetScale), (int)(400 * AssetScale), (int)(130 * AssetScale)),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        // 不可点击状态的树脂切换按钮
        ResinSwitchBtnNoActiveRo = new RecognitionObject
        {
            Name = "ResinSwitchBtnNoActive",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoDomain", "resin_switch_btn_no_active.png", this.systemInfo),
            RegionOfInterest = new Rect((int)(960 * AssetScale), (int)(430 * AssetScale), (int)(400 * AssetScale), (int)(130 * AssetScale)),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
    }
}