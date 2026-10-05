namespace BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;

public class AutoPickExternalConfig
{

    // 需要F的文本（对话、拾取）
    public string[] TextList { get; set; } = [];

    // 无视文本和图标遇到F就点击
    public bool ForceInteraction { get; set; } = false;

    /// <summary>
    /// 任务期内覆盖 OCR 名单拾取。默认 OCR，与全局实时拾取一致。
    /// </summary>
    public AutoPickRuntimeMode RuntimeMode { get; set; } = AutoPickRuntimeMode.Ocr;
}
