using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 远程节点操作类型
/// </summary>
public enum PuloniaRemoteNodeKind
{
    /// <summary>
    /// 执行一个快照叶子节点
    /// </summary>
    Task,

    /// <summary>
    /// 运行开始前在执行实例的游戏中准备并核验账号
    /// </summary>
    AccountPrepare
}

/// <summary>
/// 远程节点的结束方式
/// </summary>
public enum PuloniaRemoteNodeResultKind
{
    /// <summary>
    /// 执行器正常返回结果（结果本身可能是失败）
    /// </summary>
    Completed,

    /// <summary>
    /// 账号准备或核验失败，不能继续业务操作
    /// </summary>
    AccountFailed,

    /// <summary>
    /// 执行器或执行实例基础设施抛出异常
    /// </summary>
    Failed,

    /// <summary>
    /// 按主实例的取消请求退出
    /// </summary>
    Cancelled,

    /// <summary>
    /// 执行实例本地停止（停止热键、截图器停止或应用关闭）
    /// </summary>
    CancelledByTarget
}

/// <summary>
/// pulonia.node.start：主实例请求子实例执行一个节点。子实例立即确认，在后台执行。
/// Pulonia 模型先用 PuloniaTaskJson 序列化成字符串，避开 IPC 序列化器与 Pulonia 严格格式的差异
/// </summary>
public sealed class PuloniaRemoteNodeStart
{
    /// <summary>
    /// 本次远程执行的唯一标识，结束回报与取消都用它关联
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public PuloniaRemoteNodeKind Kind { get; init; }

    /// <summary>
    /// 所属请求 ID
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// 所属运行 ID
    /// </summary>
    public Guid RunId { get; init; }

    /// <summary>
    /// 节点从 1 开始的尝试次数
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// 节点地址
    /// </summary>
    public string TaskAddress { get; init; } = string.Empty;

    /// <summary>
    /// 节点 JSON（PuloniaTaskPreparedTask）；账号准备时为空
    /// </summary>
    public string? TaskJson { get; init; }

    /// <summary>
    /// 运行快照 JSON；账号准备时为空
    /// </summary>
    public string? SnapshotJson { get; init; }

    /// <summary>
    /// 节点是否需要游戏会话（任务锁、运行环境与任务模式）
    /// </summary>
    public bool RequiresGameSession { get; init; }

    /// <summary>
    /// 本次运行已核验的 UID；游戏节点执行前在执行实例中再次核验，账号准备时用于续跑核验
    /// </summary>
    public string? ExpectedUid { get; init; }

    /// <summary>
    /// 账号资料 JSON（PuloniaGenshinAccountProfile），只用于账号准备
    /// </summary>
    public string? AccountJson { get; init; }
}

/// <summary>
/// pulonia.node.cancel：主实例请求子实例取消一个远程节点
/// </summary>
public sealed class PuloniaRemoteNodeCancel
{
    /// <summary>
    /// 要取消的远程执行标识
    /// </summary>
    public Guid ExecutionId { get; init; }
}

/// <summary>
/// pulonia.node.report：子实例转发执行器提交的完成事件，主实例写入账本后才确认
/// </summary>
public sealed class PuloniaRemoteNodeReport
{
    /// <summary>
    /// 所属远程执行标识
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 完成事件 JSON（PuloniaTaskCompletionEvent）
    /// </summary>
    public string EventJson { get; init; } = string.Empty;
}

/// <summary>
/// pulonia.node.finished：子实例回报节点结束
/// </summary>
public sealed class PuloniaRemoteNodeFinished
{
    /// <summary>
    /// 所属远程执行标识
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 结束方式
    /// </summary>
    public PuloniaRemoteNodeResultKind ResultKind { get; init; }

    /// <summary>
    /// 结果摘要或失败原因
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 执行器结果种类，只在 Completed 时有意义
    /// </summary>
    public PuloniaTaskOutcomeKind OutcomeKind { get; init; }

    /// <summary>
    /// 执行器结果数据 JSON
    /// </summary>
    public string? OutcomeDataJson { get; init; }

    /// <summary>
    /// 执行器结果证据 JSON
    /// </summary>
    public string? OutcomeEvidenceJson { get; init; }

    /// <summary>
    /// 执行器结果原因码
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// 账号准备核验得到的完整 UID
    /// </summary>
    public string? VerifiedUid { get; init; }

    /// <summary>
    /// 把执行器结果转换为可传输的结束回报
    /// </summary>
    /// <param name="executionId">远程执行标识</param>
    /// <param name="outcome">执行器结果</param>
    /// <returns>结束回报</returns>
    public static PuloniaRemoteNodeFinished FromOutcome(Guid executionId, PuloniaTaskOutcome outcome) => new()
    {
        ExecutionId = executionId,
        ResultKind = PuloniaRemoteNodeResultKind.Completed,
        Message = outcome.Message,
        OutcomeKind = outcome.Kind,
        OutcomeDataJson = outcome.Data.ToString(Formatting.None),
        OutcomeEvidenceJson = PuloniaTaskJson.Write(new PuloniaRemoteEvidenceList { Items = [.. outcome.Evidence] }),
        ReasonCode = outcome.ReasonCode
    };

    /// <summary>
    /// 还原执行器结果
    /// </summary>
    /// <returns>执行器结果</returns>
    /// <exception cref="JsonException">数据或证据 JSON 无效</exception>
    public PuloniaTaskOutcome ToOutcome()
    {
        var data = string.IsNullOrEmpty(OutcomeDataJson) ? null : ParseObject(OutcomeDataJson);
        var evidence = string.IsNullOrEmpty(OutcomeEvidenceJson)
            ? []
            : PuloniaTaskJson.Read<PuloniaRemoteEvidenceList>(OutcomeEvidenceJson).Items;
        return new PuloniaTaskOutcome(OutcomeKind, Message, data, evidence, ReasonCode);
    }

    /// <summary>
    /// 不把形如日期的字符串改写为日期值，与本地执行器返回的数据保持一致
    /// </summary>
    private static JObject ParseObject(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }
}

/// <summary>
/// 证据列表的 JSON 外壳：PuloniaTaskJson 只读取对象根
/// </summary>
internal sealed class PuloniaRemoteEvidenceList
{
    /// <summary>
    /// 证据项
    /// </summary>
    public List<PuloniaTaskEvidence> Items { get; set; } = [];
}
