using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>任务计划编辑器展开状态的独立持久化；不改写计划修订，也不进入运行快照。</summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>与 plan-order.json 同级的编辑器界面状态文件名。</summary>
    private const string PlanViewStateFileName = "plan-view-state.json";

    /// <summary>界面状态允许记录的计划数与单计划节点数上限，与计划目录和计划节点上限一致。</summary>
    private const int MaxViewStateEntries = 10000;

    /// <summary>
    /// 读取编辑器展开状态；文件尚未建立时返回全部展开的空状态。
    /// 已有文件损坏时明确报错，由调用方决定是否回退为全部展开。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async Task<PuloniaTaskPlanViewState> LoadPlanViewStateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await ReadIfExistsAsync(Path.Combine(RootDirectory, PlanViewStateFileName), ct).ConfigureAwait(false);
            if (json is null)
                return new PuloniaTaskPlanViewState();
            var state = PuloniaTaskJson.Read<PuloniaTaskPlanViewState>(json);
            ValidatePlanViewState(state);
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 保存编辑器展开状态；后写者胜出，失败只影响下次打开时的展开显示。
    /// </summary>
    /// <param name="state">展开状态。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SavePlanViewStateAsync(PuloniaTaskPlanViewState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidatePlanViewState(state);
        var json = PuloniaTaskJson.Write(state);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            await WriteAtomicallyAsync(Path.Combine(RootDirectory, PlanViewStateFileName), json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>校验格式版本、数量上限和 ID 格式，避免损坏元数据进入编辑器。</summary>
    /// <param name="state">展开状态。</param>
    private static void ValidatePlanViewState(PuloniaTaskPlanViewState state)
    {
        if (state.SchemaVersion != PuloniaTaskPlanViewState.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException(PlanViewStateFileName, $"不支持编辑器状态格式版本 {state.SchemaVersion}。");
        if (state.CollapsedTaskIds is null || state.CollapsedTaskIds.Count > MaxViewStateEntries)
            throw new PuloniaTaskValidationException(PlanViewStateFileName, "编辑器状态为空或超过数量上限。");
        foreach (var (planId, taskIds) in state.CollapsedTaskIds)
        {
            PuloniaTaskValidator.ValidateId(planId, PlanViewStateFileName);
            if (taskIds is null || taskIds.Count > MaxViewStateEntries)
                throw new PuloniaTaskValidationException(PlanViewStateFileName, "折叠节点列表为空或超过数量上限。");
            foreach (var taskId in taskIds)
                PuloniaTaskValidator.ValidateId(taskId, PlanViewStateFileName);
        }
    }
}
