using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>协调器内部的可变运行状态。</summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>
    /// 仅在协调器内部可变的一次未归档运行（排队、执行中或结束后未能归档）。
    /// 可变字段的写入以及 <see cref="ToRecord"/>、<see cref="CreateView"/> 都要求调用方持有 <see cref="_runsGate"/>；
    /// 只由本运行执行流程写入、随后又由同一流程读取的字段（如 <see cref="VerifiedUid"/>）可以在锁外读取。
    /// </summary>
    private sealed class RunState
    {
        /// <summary>
        /// 运行 ID；提交、查询、取消和历史都使用此身份。
        /// </summary>
        public string RunId { get; }

        /// <summary>
        /// 固定后的调用请求；构造后不再修改，持久记录和视图直接共用。
        /// </summary>
        public PuloniaTaskRequest Request { get; }

        /// <summary>
        /// 提交时的计划名称。
        /// </summary>
        public string PlanName { get; }

        /// <summary>
        /// 提交时固定的运行快照。
        /// </summary>
        public PuloniaTaskSnapshot Snapshot { get; }

        /// <summary>
        /// 已序列化的快照，写入状态和历史时无需重复序列化。
        /// </summary>
        public string SnapshotJson { get; }

        /// <summary>
        /// 快照固定的账号是否为需要切换的指定账号（不是默认账号）；决定路线 CD 是否按核验 UID 隔离。
        /// </summary>
        public bool UsesSwitchedAccount { get; }

        /// <summary>
        /// 显式取消当前运行的令牌源。
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>
        /// 最终状态等待源，异步 continuation 不在状态锁内执行。
        /// </summary>
        public TaskCompletionSource<PuloniaTaskRunView> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 已完成的节点尝试结果。
        /// </summary>
        public List<PuloniaTaskNodeResult> NodeResults { get; } = [];

        /// <summary>
        /// 每次节点尝试已落盘的确认事件，随运行写入不可变历史。
        /// </summary>
        public List<PuloniaTaskConfirmedEffect> ConfirmedEffects { get; } = [];

        /// <summary>
        /// 续跑时无需再次执行的节点地址。
        /// </summary>
        public HashSet<string> CompletedTaskAddresses { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 显式续跑起点。
        /// </summary>
        public string? ResumeFromTaskAddress { get; }

        /// <summary>
        /// 本次运行关联的历史运行 ID。
        /// </summary>
        public string? ResumedFromRunId { get; }

        /// <summary>
        /// 当前运行状态。
        /// </summary>
        public PuloniaTaskRunStatus Status { get; set; } = PuloniaTaskRunStatus.Queued;

        /// <summary>
        /// 当前节点地址；只在内存中更新，不为它单独落盘（崩溃恢复依靠快照与节点结果）。
        /// </summary>
        public string? CurrentTaskAddress { get; set; }

        /// <summary>
        /// 状态摘要；执行期间是面向用户的进度文字，逻辑判断不读取它。
        /// </summary>
        public string Message { get; set; } = "运行快照已固定，正在等待串行执行。";

        /// <summary>
        /// 提交时间。
        /// </summary>
        public DateTimeOffset SubmittedAt { get; private init; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 开始时间。
        /// </summary>
        public DateTimeOffset? StartedAt { get; set; }

        /// <summary>
        /// 完成时间。
        /// </summary>
        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>
        /// 最近一个在重试耗尽后失败的节点的失败说明；为空表示没有节点最终失败。正常走完计划时据此判定失败。
        /// </summary>
        public string? FailureMessage { get; set; }

        /// <summary>当前运行已耐久保存的实际 UID，编辑资料不能改写此身份。</summary>
        public string? VerifiedUid { get; set; }

        /// <summary>
        /// 建立排队状态。
        /// </summary>
        /// <param name="runId">运行 ID。</param>
        /// <param name="request">已校验的请求。</param>
        /// <param name="planName">计划名称。</param>
        /// <param name="snapshot">固定快照。</param>
        /// <param name="snapshotJson">快照 JSON；为空时由快照序列化。</param>
        /// <param name="completedTaskAddresses">续跑时跳过的地址。</param>
        /// <param name="resumeFromTaskAddress">显式续跑起点。</param>
        /// <param name="resumedFromRunId">来源历史运行 ID。</param>
        public RunState(string runId, PuloniaTaskRequest request, string planName,
            PuloniaTaskSnapshot snapshot, string? snapshotJson = null, IEnumerable<string>? completedTaskAddresses = null,
            string? resumeFromTaskAddress = null, string? resumedFromRunId = null)
        {
            RunId = runId;
            Request = request;
            PlanName = planName;
            Snapshot = snapshot;
            SnapshotJson = snapshotJson ?? snapshot.ToJson();
            UsesSwitchedAccount = IsSwitchedAccount(snapshot);
            foreach (var address in completedTaskAddresses ?? [])
                CompletedTaskAddresses.Add(address);
            ResumeFromTaskAddress = resumeFromTaskAddress;
            ResumedFromRunId = resumedFromRunId;
        }

        /// <summary>
        /// 从 state.json 恢复尚未归档的运行；记录已由读取时校验，这里只复制，不重复校验请求。
        /// </summary>
        /// <param name="record">排队请求或未能归档的结束运行。</param>
        public static RunState FromRecord(PuloniaTaskRunRecord record)
        {
            var state = new RunState(record.RunId, record.Request, record.PlanName,
                PuloniaTaskJson.ReadSnapshot(record.SnapshotJson), record.SnapshotJson, record.CompletedTaskAddresses,
                record.ResumeFromTaskAddress, record.ResumedFromRunId)
            {
                Status = record.Status,
                CurrentTaskAddress = record.CurrentTaskAddress,
                Message = record.Message,
                SubmittedAt = record.SubmittedAt,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                VerifiedUid = record.VerifiedUid
            };
            state.NodeResults.AddRange(record.NodeResults);
            state.ConfirmedEffects.AddRange(record.ConfirmedEffects);
            return state;
        }

        /// <summary>
        /// 建立可写入当前状态或不可变历史的记录：集合是新副本，请求、快照文本和集合元素沿用同一不可变实例。
        /// 调用方必须持有 <see cref="_runsGate"/>。
        /// </summary>
        public PuloniaTaskRunRecord ToRecord() => new()
        {
            VerifiedUid = VerifiedUid,
            RunId = RunId,
            Request = Request,
            PlanName = PlanName,
            SnapshotJson = SnapshotJson,
            Status = Status,
            CurrentTaskAddress = CurrentTaskAddress,
            Message = Message,
            SubmittedAt = SubmittedAt,
            StartedAt = StartedAt,
            FinishedAt = FinishedAt,
            NodeResults = [.. NodeResults],
            ConfirmedEffects = [.. ConfirmedEffects],
            CompletedTaskAddresses = [.. CompletedTaskAddresses],
            ResumeFromTaskAddress = ResumeFromTaskAddress,
            ResumedFromRunId = ResumedFromRunId
        };

        /// <summary>
        /// 建立当前状态的不可变视图。调用方必须持有 <see cref="_runsGate"/>。
        /// </summary>
        public PuloniaTaskRunView CreateView() => new(ToRecord(), isHistorical: false);
    }
}
