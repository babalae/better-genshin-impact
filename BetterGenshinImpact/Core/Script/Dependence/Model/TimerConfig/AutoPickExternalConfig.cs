namespace BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;

public class AutoPickExternalConfig
{
    // 关闭黑白名单
    public bool DisabledBlackWhiteList { get; set; } = false;

    // 需要F的文本（对话、拾取）
    public string[] TextList { get; set; } = [];

    // 无视文本和图标遇到F就点击
    public bool ForceInteraction { get; set; } = false;
}
