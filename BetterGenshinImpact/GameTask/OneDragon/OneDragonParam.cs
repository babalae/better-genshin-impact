namespace BetterGenshinImpact.GameTask.OneDragon;

/// <summary>
/// 一条龙任务参数。任务内容由目标实例从共享的 User\OneDragon 目录按配置名读取
/// </summary>
public class OneDragonParam
{
    /// <summary>
    /// 要执行的一条龙配置名；为空时使用配置根中当前选中的一条龙配置
    /// </summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>
    /// 从这个任务 Id 开始执行（"从此执行"标记，由发起方读取后清除）；为空表示从头开始
    /// </summary>
    public string? StartTaskId { get; set; }
}
