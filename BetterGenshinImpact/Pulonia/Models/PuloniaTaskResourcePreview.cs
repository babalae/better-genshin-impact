namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 按需读取的一项资源详情及推荐任务名称。
/// </summary>
public sealed class PuloniaTaskResourcePreview
{
    /// <summary>
    /// 面向用户展示的详情文本。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 资源元数据提供的推荐任务名称。
    /// </summary>
    public string? SuggestedName { get; }

    /// <summary>
    /// 建立不可变资源预览。
    /// </summary>
    public PuloniaTaskResourcePreview(string text, string? suggestedName)
    {
        Text = text;
        SuggestedName = suggestedName;
    }
}
