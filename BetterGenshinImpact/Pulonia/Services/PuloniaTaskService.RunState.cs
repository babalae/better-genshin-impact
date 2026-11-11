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
        /// 固定后的调用请求；请求对象本身不再修改，持久记录和视图直接共用。
        /// 运行开始确定运行账户后，整体替换为服务器偏移改为账户服务器时区的副本（见 <see cref="UseServerUtcOffset"/>）。
        /// </summary>
        public PuloniaTaskRequest Request { get; private set; }

        /// <summary>
        /// 执行实例键：提交或续跑时取本实例，写入运行记录的 <c>instance</c>。
        /// </summary>
        public required string Instance { get; init; }

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
        /// 本次完成的路线：完成记录已写入账户目录后，随节点结果一起落盘到运行状态，再随运行写入不可变历史。
        /// </summary>
        public List<PuloniaRouteCompletion> RouteCompletions { get; } = [];

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

        /// <summary>指定账户的运行已耐久保存的实际 UID，编辑资料不能改写此身份；跟随当前账户时为空。</summary>
        public string? VerifiedUid { get; set; }

        /// <summary>
        /// 运行账户 ID，运行开始时确定；续跑的运行在开始前保存原运行的运行账户，用于核对实例当前账户。
        /// </summary>
        public string? AccountId { get; set; }

        /// <summary>运行账户的来源；未确定时为空。</summary>
        public PuloniaTaskAccountSource? AccountSource { get; set; }

        /// <summary>跟随当前账户时的 UID 比对结果；指定账户时为空。</summary>
        public PuloniaTaskUidCheck? UidCheck { get; set; }

        /// <summary>UID 比对不一致时游戏中读到的 UID。</summary>
        public string? ObservedUid { get; set; }

        /// <summary>
        /// 运行账户的占用句柄（统筹设计 4.5）：确定运行账户后取得，运行执行流程结束时释放；只在内存中，不落盘。
        /// 只由本运行的执行流程读写。
        /// </summary>
        public IDisposable? AccountOccupancy { get; set; }

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
                Instance = record.Instance,
                Status = record.Status,
                CurrentTaskAddress = record.CurrentTaskAddress,
                Message = record.Message,
                SubmittedAt = record.SubmittedAt,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                VerifiedUid = record.VerifiedUid,
                AccountId = record.AccountId,
                AccountSource = record.AccountSource,
                UidCheck = record.UidCheck,
                ObservedUid = record.ObservedUid
            };
            state.NodeResults.AddRange(record.NodeResults);
            state.RouteCompletions.AddRange(record.RouteCompletions);
            return state;
        }

        /// <summary>
        /// 把请求换成服务器偏移为指定值的副本；其余字段与原请求共用同一对象（参数覆盖不再修改）。
        /// 调用方必须持有 <see cref="_runsGate"/>。
        /// </summary>
        /// <param name="serverUtcOffsetMinutes">运行账户服务器时区的分钟偏移。</param>
        public void UseServerUtcOffset(int serverUtcOffsetMinutes)
        {
            if (Request.ServerUtcOffsetMinutes == serverUtcOffsetMinutes)
                return;
            var request = Request;
            Request = new PuloniaTaskRequest
            {
                PlanId = request.PlanId,
                ServerUtcOffsetMinutes = serverUtcOffsetMinutes,
                TimeoutSeconds = request.TimeoutSeconds,
                ParameterOverrides = request.ParameterOverrides,
                Source = request.Source,
                TargetTaskId = request.TargetTaskId,
                TriggerId = request.TriggerId,
                OccurrenceUtc = request.OccurrenceUtc,
                DeadlineUtc = request.DeadlineUtc,
                TriggerActivatedAtUtc = request.TriggerActivatedAtUtc,
                BusyPolicy = request.BusyPolicy,
                AllowDisplaceOnlineInstance = request.AllowDisplaceOnlineInstance
            };
        }

        /// <summary>
        /// 建立可写入当前状态或不可变历史的记录：集合是新副本，请求、快照文本和集合元素沿用同一不可变实例。
        /// 调用方必须持有 <see cref="_runsGate"/>。
        /// </summary>
        public PuloniaTaskRunRecord ToRecord() => new()
        {
            Instance = Instance,
            VerifiedUid = VerifiedUid,
            AccountId = AccountId,
            AccountSource = AccountSource,
            UidCheck = UidCheck,
            ObservedUid = ObservedUid,
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
            RouteCompletions = [.. RouteCompletions],
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
