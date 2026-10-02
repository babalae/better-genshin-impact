using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 具体任务执行器返回的结构化结果。
/// </summary>
public sealed class PuloniaTaskOutcome
{
    /// <summary>
    /// 本次动作是否成功完成。
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 面向调用端的结果原因或摘要。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 执行器附带的结构化结果副本。
    /// </summary>
    public JObject Data { get; }

    /// <summary>
    /// 建立不可变执行结果，并复制调用方提供的数据。
    /// </summary>
    public PuloniaTaskOutcome(bool isSuccess, string message, JObject? data = null)
    {
        IsSuccess = isSuccess;
        Message = message;
        Data = data is null ? new JObject() : (JObject)data.DeepClone();
    }

    /// <summary>
    /// 建立成功结果。
    /// </summary>
    public static PuloniaTaskOutcome Success(string message, JObject? data = null) => new(true, message, data);

    /// <summary>
    /// 建立失败结果。
    /// </summary>
    public static PuloniaTaskOutcome Failure(string message, JObject? data = null) => new(false, message, data);
}
