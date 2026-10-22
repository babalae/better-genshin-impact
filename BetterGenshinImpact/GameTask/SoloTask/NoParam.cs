namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 没有业务参数的任务使用的空参数。
/// </summary>
public sealed class NoParam
{
    /// <summary>
    /// 共享的空参数实例。
    /// </summary>
    public static NoParam Instance { get; } = new();
}
