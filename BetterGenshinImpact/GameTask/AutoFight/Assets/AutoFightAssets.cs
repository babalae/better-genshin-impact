using BetterGenshinImpact.Core.Recognition;

namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public class AutoFightAssets
{
    public RecognitionObject WandererIconRa;

    public AutoFightAssets()
    {
        var info = TaskContext.Instance().SystemInfo;
        WandererIconRa = new RecognitionObject
        {
            Name = "WandererIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "wanderer_icon.png"), 
            DrawOnWindow = false
        }.InitTemplate();
    }   
}