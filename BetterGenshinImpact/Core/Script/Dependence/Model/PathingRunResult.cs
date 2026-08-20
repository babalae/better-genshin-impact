namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 地图追踪执行结果，作为 JS 调用 pathingScript 的返回值
/// </summary>
public class PathingRunResult
{
    /// <summary>
    /// 是否成功完成地图追踪
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 执行结果状态
    /// </summary>
    public string Status { get; set; } = PathingRunStatus.Failed;

    /// <summary>
    /// 结果描述（失败原因等）
    /// </summary>
    public string Message { get; set; } = "";

    public static PathingRunResult Ok(string message = "地图追踪执行成功")
    {
        return new PathingRunResult
        {
            Success = true,
            Status = PathingRunStatus.Success,
            Message = message
        };
    }

    public static PathingRunResult Fail(string message, string status = PathingRunStatus.Failed)
    {
        return new PathingRunResult
        {
            Success = false,
            Status = status,
            Message = message
        };
    }
}

/// <summary>
/// 地图追踪执行结果状态常量
/// </summary>
public static class PathingRunStatus
{
    /// <summary>
    /// 成功完成
    /// </summary>
    public const string Success = "Success";

    /// <summary>
    /// 执行被取消
    /// </summary>
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// 路径 JSON 解析失败
    /// </summary>
    public const string JsonParseError = "JsonParseError";

    /// <summary>
    /// 路径文件读取失败
    /// </summary>
    public const string FileReadError = "FileReadError";

    /// <summary>
    /// 执行失败
    /// </summary>
    public const string Failed = "Failed";
}
