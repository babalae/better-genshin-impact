namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 执行详情的一项可选择、可复制的文本内容。
/// </summary>
public sealed record PuloniaTaskHistoryField
{
    /// <summary>
    /// 字段的中文标题。
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// 字段的完整内容；日志和 JSON 可以包含多行。
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 建立只读详情字段。
    /// </summary>
    public PuloniaTaskHistoryField(string label, string value)
    {
        Label = label;
        Value = value;
    }
}
