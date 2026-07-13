namespace BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;

public class AutoPickExternalConfig
{

    // 需要F的文本（对话、拾取）
    public string[] TextList { get; set; } = [];

    // 无视文本和图标遇到F就点击
    public bool ForceInteraction { get; set; } = false;
}
