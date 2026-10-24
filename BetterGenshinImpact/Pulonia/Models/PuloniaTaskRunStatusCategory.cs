namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 运行状态的语义分类，供列表与详情按类别区分展示，不参与调度判断。
/// </summary>
public enum PuloniaTaskRunStatusCategory
{
    /// <summary>排队、已停止、已过期等中性状态。</summary>
    Neutral,

    /// <summary>运行中或停止中，仍属于未结束的活跃运行。</summary>
    Running,

    /// <summary>运行已完成。</summary>
    Success,

    /// <summary>运行失败或超时。</summary>
    Failed,

    /// <summary>待处理或已中断，需要人工续跑或核对。</summary>
    Attention
}
