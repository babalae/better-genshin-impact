namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 地图追踪执行结果，作为 JS 调用 pathingScript 的返回值
/// </summary>
public class PathingRunResult
{
    /// <summary>
    /// 是否真正正常完整运行完成（取消、中途放弃、解析/读取失败、异常等均视为失败）
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 结果描述（失败原因等）
    /// </summary>
    public string Message { get; set; } = "";

    public static PathingRunResult Ok(string message = "地图追踪执行成功")
    {
        return new PathingRunResult
        {
            Success = true,
            Message = message
        };
    }

    public static PathingRunResult Fail(string message)
    {
        return new PathingRunResult
        {
            Success = false,
            Message = message
        };
    }
}
