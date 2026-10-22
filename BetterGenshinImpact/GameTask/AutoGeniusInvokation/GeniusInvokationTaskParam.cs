using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

/// <summary>
/// 自动七圣召唤任务参数
/// </summary>
public class GeniusInvokationTaskParam : BaseTaskParam<AutoGeniusInvokationTask>
{
    /// <summary>
    /// 创建空策略的参数，供 JSON 反序列化和默认参数使用
    /// </summary>
    public GeniusInvokationTaskParam() : base(null, null)
    {
    }

    /// <summary>
    /// 使用指定策略文本创建参数
    /// </summary>
    /// <param name="strategyContent">七圣召唤策略文本</param>
    public GeniusInvokationTaskParam(string strategyContent) : base(null, null)
    {
        this.StrategyContent = strategyContent;
    }

    /// <summary>
    /// 七圣召唤策略文本（策略文件的完整内容）；为空时任务返回参数无效
    /// </summary>
    public string StrategyContent { get; set; } = "";
}
