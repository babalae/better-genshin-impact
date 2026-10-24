using System;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 为 Pulonia 持久化实体生成带类型前缀的短稳定 ID；字母表与编码由 <see cref="ShortId"/> 统一提供。
/// </summary>
public static class PuloniaId
{
    /// <summary>
    /// 运行 ID 末尾随机部分的字符数；同一秒内另由单写入者查重。
    /// </summary>
    private const int RunRandomLength = 4;

    /// <summary>
    /// 生成任务计划 ID。
    /// </summary>
    public static string NewPlanId() => ShortId.Create("pln");

    /// <summary>
    /// 生成任务节点 ID。
    /// </summary>
    public static string NewTaskId() => ShortId.Create("tsk");

    /// <summary>
    /// 生成共享参数预设 ID。
    /// </summary>
    public static string NewPresetId() => ShortId.Create("pre");

    /// <summary>
    /// 生成任务触发器 ID。
    /// </summary>
    public static string NewTriggerId() => ShortId.Create("trg");

    /// <summary>
    /// 生成游戏账号资料 ID。
    /// </summary>
    public static string NewAccountId() => ShortId.Create("acc");

    /// <summary>
    /// 生成运行 ID：<c>run_本地时间-4 位随机</c>，便于在历史目录中按文件名辨认先后；排序仍以提交时间为准。
    /// </summary>
    /// <param name="localTime">生成时刻，按本地时间格式化</param>
    public static string NewRunId(DateTimeOffset localTime)
        => "run_" + localTime.ToLocalTime().ToString("yyyyMMdd-HHmmss") + "-" + ShortId.Create(null, RunRandomLength);

    /// <summary>
    /// 由目录内规范化相对路径派生展开节点 ID；同一路径始终得到同一 ID，与内容、顺序和组名无关。
    /// </summary>
    /// <param name="relativePath">相对引用目录的资源路径</param>
    public static string FromResourcePath(string relativePath)
        => ShortId.FromText("res", relativePath.ToLowerInvariant());
}
