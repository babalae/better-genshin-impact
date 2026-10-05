using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 统一的 JSON 读写与复制入口，保持参数类型并拒绝隐式格式迁移。
/// </summary>
public static class PuloniaTaskJson
{
    /// <summary>
    /// 从 JSON 读取并校验当前计划格式；不在缺少身份时生成新 ID。
    /// </summary>
    public static PuloniaTaskPlan ReadPlan(string json)
    {
        var plan = Read<PuloniaTaskPlan>(json);
        PuloniaTaskValidator.ValidatePlan(plan);
        return plan;
    }

    /// <summary>
    /// 校验后输出计划 JSON。
    /// </summary>
    public static string WritePlan(PuloniaTaskPlan plan)
    {
        PuloniaTaskValidator.ValidatePlan(plan);
        return Write(plan);
    }

    /// <summary>
    /// 从 JSON 读取并校验参数预设。
    /// </summary>
    public static PuloniaTaskPreset ReadPreset(string json)
    {
        var preset = Read<PuloniaTaskPreset>(json);
        PuloniaTaskValidator.ValidatePreset(preset);
        return preset;
    }

    /// <summary>
    /// 校验后输出预设 JSON。
    /// </summary>
    public static string WritePreset(PuloniaTaskPreset preset)
    {
        PuloniaTaskValidator.ValidatePreset(preset);
        return Write(preset);
    }

    /// <summary>
    /// 深复制计划，保留全部稳定 ID；运行准备使用此入口。
    /// </summary>
    public static PuloniaTaskPlan ClonePlan(PuloniaTaskPlan plan) => ReadPlan(WritePlan(plan));

    /// <summary>
    /// 复制整份计划并重建身份及内部节点引用；副本触发器默认禁用，避免重复自动执行。
    /// </summary>
    public static PuloniaTaskPlan CopyPlan(PuloniaTaskPlan plan)
    {
        var copy = ClonePlan(plan);
        copy.Id = PuloniaId.NewPlanId();
        copy.Revision = 0;

        // 节点身份改变后，账号预设选择和触发目标必须同步指向副本节点。
        var taskIds = new Dictionary<string, string>(StringComparer.Ordinal);
        AssignNewIds(copy.RootTask, taskIds);
        foreach (var account in copy.Accounts)
            account.PresetSelections = account.PresetSelections.ToDictionary(
                item => taskIds[item.Key], item => item.Value, StringComparer.Ordinal);
        foreach (var trigger in copy.Triggers)
        {
            trigger.Id = PuloniaId.NewTriggerId();
            trigger.Enabled = false;
            if (trigger.TargetTaskId is { } targetId)
                trigger.TargetTaskId = taskIds[targetId];
        }
        PuloniaTaskValidator.ValidatePlan(copy);
        return copy;
    }

    /// <summary>
    /// 读取运行准备样例的显式选项，业务模型与选项使用相同 JSON 类型规则。
    /// </summary>
    public static PuloniaTaskBuildOptions ReadBuildOptions(string json) => Read<PuloniaTaskBuildOptions>(json);

    /// <summary>
    /// 从历史记录恢复提交时固定的不可变运行快照。
    /// </summary>
    public static PuloniaTaskSnapshot ReadSnapshot(string json) => Read<PuloniaTaskSnapshot>(json);

    /// <summary>
    /// 读取并校验当前运行状态文件。
    /// </summary>
    public static PuloniaTaskState ReadState(string json)
    {
        var state = Read<PuloniaTaskState>(json);
        if (state.SchemaVersion != PuloniaTaskState.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException("state.json",
                $"不支持运行状态格式版本 {state.SchemaVersion}。");
        if (state.Sequence < 0 || state.PendingRequests is null || state.PendingArchives is null
            || state.Ledger is null || state.UncertainOperations is null || state.TriggerStates is null)
            throw new PuloniaTaskValidationException("state.json", "运行状态内容不完整或序列无效。");
        if (state.PendingRequests.Count > 10000 || state.PendingArchives.Count > 10000
            || state.Ledger.Count > 100000 || state.UncertainOperations.Count > 10000 || state.TriggerStates.Count > 10000)
            throw new PuloniaTaskValidationException("state.json", "运行状态集合超过支持上限。");
        var requestIds = new HashSet<Guid>();
        var triggerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in state.TriggerStates)
        {
            if (trigger is null || !triggerKeys.Add(trigger.PlanId + "/" + trigger.TriggerId))
                throw new PuloniaTaskValidationException("state.json/trigger_states", "触发器状态为空或重复。");
            PuloniaTaskValidator.ValidateId(trigger.PlanId, "trigger_state/plan_id");
            PuloniaTaskValidator.ValidateId(trigger.TriggerId, "trigger_state/trigger_id");
            if (trigger.Signature is null || trigger.Signature.Length != 64 || !trigger.Signature.All(Uri.IsHexDigit)
                || trigger.LastRequestId == Guid.Empty || string.IsNullOrWhiteSpace(trigger.Status))
                throw new PuloniaTaskValidationException("state.json/trigger_states", "触发器签名、请求 ID 或状态无效。");
        }
        var runIds = new HashSet<Guid>();
        foreach (var record in state.PendingRequests)
        {
            ValidateRunRecord(record, "state.json/pending_requests");
            if (record.Status != PuloniaTaskRunStatus.Queued)
                throw new PuloniaTaskValidationException("state.json", "待执行请求必须处于排队状态。");
            AddRunIdentity(record, requestIds, runIds, "state.json");
        }
        if (state.ActiveRun is { } active)
        {
            ValidateRunRecord(active, "state.json/active_run");
            if (active.Status is not (PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling))
                throw new PuloniaTaskValidationException("state.json", "活动运行状态无效。");
            AddRunIdentity(active, requestIds, runIds, "state.json");
        }
        foreach (var record in state.PendingArchives)
        {
            ValidateRunRecord(record, "state.json/pending_archives");
            if (record.Status is PuloniaTaskRunStatus.Queued or PuloniaTaskRunStatus.Running
                or PuloniaTaskRunStatus.Cancelling)
                throw new PuloniaTaskValidationException("state.json", "待归档运行尚未结束。");
            AddRunIdentity(record, requestIds, runIds, "state.json");
        }
        var eventKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in state.Ledger)
        {
            if (string.IsNullOrWhiteSpace(entry.EventKey) || !eventKeys.Add(entry.EventKey)
                || string.IsNullOrWhiteSpace(entry.ScopeKey) || string.IsNullOrWhiteSpace(entry.EffectKey)
                || string.IsNullOrWhiteSpace(entry.WindowKey) || entry.Units <= 0
                || string.IsNullOrWhiteSpace(entry.TaskRunId) || entry.Evidence is null)
                throw new PuloniaTaskValidationException("state.json/ledger", "账本项字段无效或事件键重复。");
        }
        foreach (var intent in state.UncertainOperations)
        {
            if (intent.RunId == Guid.Empty || string.IsNullOrWhiteSpace(intent.TaskAddress)
                || intent.RuleIds is null || intent.Reservations is null || intent.RuleIds.Count == 0)
                throw new PuloniaTaskValidationException("state.json/uncertain_operations", "未决操作字段无效。");
        }
        return state;
    }

    /// <summary>
    /// 输出当前运行状态文件。
    /// </summary>
    public static string WriteState(PuloniaTaskState state) => Write(state);

    /// <summary>
    /// 读取不可变历史记录。
    /// </summary>
    public static PuloniaTaskRunRecord ReadRunRecord(string json)
    {
        var record = Read<PuloniaTaskRunRecord>(json);
        ValidateRunRecord(record, "history");
        return record;
    }

    /// <summary>
    /// 输出不可变历史记录。
    /// </summary>
    public static string WriteRunRecord(PuloniaTaskRunRecord record) => Write(record);

    /// <summary>
    /// 校验状态和历史共用的运行记录必要字段。
    /// </summary>
    private static void ValidateRunRecord(PuloniaTaskRunRecord record, string path)
    {
        if (record.RequestId == Guid.Empty || record.RunId == Guid.Empty || record.Request is null
            || string.IsNullOrWhiteSpace(record.PlanName) || string.IsNullOrWhiteSpace(record.SnapshotJson)
            || string.IsNullOrWhiteSpace(record.Message) || record.NodeResults is null
            || record.CompletedTaskAddresses is null || record.ConfirmedEffects is null)
            throw new PuloniaTaskValidationException(path, "运行记录必要字段缺失。");
        PuloniaTaskValidator.ValidateId(record.Request.PlanId, path + "/plan_id");
        if (record.Request.ParameterOverrides is null)
            throw new PuloniaTaskValidationException(path, "运行请求参数覆盖不能为空。");
        var eventKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in record.ConfirmedEffects)
        {
            // 新字段可缺省以兼容旧历史，但已保存的确认事实不能缺失身份或证据。
            if (effect is null || string.IsNullOrWhiteSpace(effect.TaskAddress) || effect.Attempt <= 0
                || effect.Entry is null || string.IsNullOrWhiteSpace(effect.Entry.EventKey)
                || string.IsNullOrWhiteSpace(effect.Entry.EffectKey) || string.IsNullOrWhiteSpace(effect.Entry.ScopeKey)
                || effect.Entry.Units <= 0 || effect.Entry.Evidence is null
                || effect.Entry.TaskRunId != $"{record.RunId:N}:{effect.TaskAddress}"
                || !eventKeys.Add(effect.Entry.EventKey))
                throw new PuloniaTaskValidationException(path, "运行确认事件身份、额度或证据无效。");
        }
    }

    /// <summary>
    /// 校验当前状态内请求与运行身份不重复。
    /// </summary>
    private static void AddRunIdentity(PuloniaTaskRunRecord record, ISet<Guid> requestIds,
        ISet<Guid> runIds, string path)
    {
        if (!requestIds.Add(record.RequestId) || !runIds.Add(record.RunId))
            throw new PuloniaTaskValidationException(path, "请求 ID 或运行 ID 在当前状态中重复。");
    }

    /// <summary>
    /// 复制子树供粘贴使用；每个节点生成新 ID，参数仍按同一格式读取。
    /// </summary>
    public static PuloniaTask CopySubtree(PuloniaTask task)
    {
        // 使用临时根进行同一套结构校验，不允许对象循环进入序列化。
        var wrapper = new PuloniaTaskPlan();
        wrapper.RootTask.Children.Add(task);
        var clone = ClonePlan(wrapper).RootTask.Children[0];
        AssignNewIds(clone);
        return clone;
    }

    /// <summary>
    /// 读取单一对象，拒绝重复键、额外根值、未知属性与过深 JSON。
    /// </summary>
    internal static T Read<T>(string json)
    {
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 256 };
        var token = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read())
            throw new JsonSerializationException("JSON 根对象后不能包含其他内容。");
        return token.ToObject<T>(JsonSerializer.Create(CreateSettings()))
               ?? throw new JsonSerializationException("JSON 对象不能为空。");
    }

    /// <summary>
    /// 固定格式输出；不使用项目的 System.Text.Json 配置。
    /// </summary>
    internal static string Write(object value) => JsonConvert.SerializeObject(value, CreateSettings());

    /// <summary>
    /// 每次创建独立序列化配置，不共享可变 JsonSerializer 实例。
    /// </summary>
    private static JsonSerializerSettings CreateSettings() => new()
    {
        Formatting = Formatting.Indented,
        DateParseHandling = DateParseHandling.None,
        TypeNameHandling = TypeNameHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        MaxDepth = 256
    };

    /// <summary>
    /// 递归更新副本身份，不改变原树。
    /// </summary>
    private static void AssignNewIds(PuloniaTask task, IDictionary<string, string>? taskIds = null)
    {
        var originalId = task.Id;
        task.Id = PuloniaId.NewTaskId();
        taskIds?.Add(originalId, task.Id);
        foreach (var child in task.Children)
            AssignNewIds(child, taskIds);
    }

}
