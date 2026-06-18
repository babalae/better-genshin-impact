using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoBoss.Assets;

public class AutoBossAssets : BaseAssets<AutoBossAssets>
{
    public RecognitionObject OriginalResinTopIconRo;
    public RecognitionObject RewardBoxRo;
    public RecognitionObject OpenResinSupplementPaneButtonRo;
    public RecognitionObject TransientResinInSupplementPaneRo;
    public RecognitionObject FragileResinInSupplementPaneRo;
    public RecognitionObject IncreaseResinUsageQuantityButtonRo;

#pragma warning disable CS8618
    private AutoBossAssets() : base()
    {
        Initialization(systemInfo);
    }

    protected AutoBossAssets(ISystemInfo systemInfo) : base(systemInfo)
    {
        Initialization(systemInfo);
    }
#pragma warning restore CS8618

    private void Initialization(ISystemInfo systemInfo)
    {
        OriginalResinTopIconRo = new RecognitionObject
        {
            Name = "AutoBossOriginalResinTopIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "original_resin_top_icon.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        RewardBoxRo = new RecognitionObject
        {
            Name = "AutoBossRewardBox",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "box.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        OpenResinSupplementPaneButtonRo = new RecognitionObject
        {
            Name = "AutoBossOpenResinSupplementPaneButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "open_resin_supplement_pane_button.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        TransientResinInSupplementPaneRo = new RecognitionObject
        {
            Name = "AutoBossTransientResinInSupplementPane",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "transient_resin_in_supplement_pane.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        FragileResinInSupplementPaneRo = new RecognitionObject
        {
            Name = "AutoBossFragileResinInSupplementPane",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "fragile_resin_in_supplement_pane.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        IncreaseResinUsageQuantityButtonRo = new RecognitionObject
        {
            Name = "AutoBossIncreaseResinUsageQuantityButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoBoss", "increase_resin_usage_quantity_button.png", systemInfo),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
    }
}
