using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 使用现有地图追踪能力执行 Pulonia 路线资源，并返回路线真实完成状态。
/// </summary>
public sealed class PuloniaPathingTaskExecutor : IPuloniaTaskExecutor
{
    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "pathing",
        DisplayName = "地图追踪",
        Description = "从已拉取的脚本仓库选择路线，并配置队伍、自动拾取与自动战斗选项。",
        RequiresGameSession = true,
        ResourceBaseDirectory = MapPathingViewModel.PathJsonPath,
        DefaultParameters = PuloniaTaskCommonSettings.CreateDefaults("pathing"),
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = CreateParameterProperties(),
            ["additionalProperties"] = false
        },
        PublicParameters = [.. PuloniaTaskCommonSettings.CreateDefaults("pathing").Properties().Select(property => property.Name)]
    }];

    /// <summary>把文件级 CD 加入地图追踪 Schema，旧公共行走设置表单仍只处理自己的字段。</summary>
    private static JObject CreateParameterProperties()
    {
        var properties = PuloniaTaskCommonSettings.CreateSchemaProperties("pathing");
        properties[PuloniaCollectionCooldown.ParameterName] = PuloniaCollectionCooldown.CreateSchema();
        return properties;
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("地图追踪节点没有固定资源路径。");
        var resourcePath = task.Resource is { } resource
            ? await ScriptRepositoryStore.Shared.MaterializeAsync(resource, "pathing", ct).ConfigureAwait(false) : task.Path;
        var pathingTask = PathingTask.BuildFromFilePath(resourcePath);
        if (pathingTask is null)
            return PuloniaTaskOutcome.Failure("路线版本高于当前 BetterGI，已拒绝执行。");

        var parameters = task.Parameters;
        var partyConfig = PuloniaTaskCommonSettings.FromParameters("pathing", parameters).PathingConfig;
        // 触发器租约仅属于当前节点，异常或取消时也会撤销。
        using var autoPick = partyConfig.AutoPickEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null) : null;
        using var autoEat = partyConfig.AutoEatEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoEat", null) : null;

        var executor = new PathExecutor(ct) { PartyConfig = partyConfig };
        var result = await executor.Pathing(pathingTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return CreateOutcome(result, task.ResourceVersion);
    }

    /// <summary>把本次路线结果对象转换为任务结果；成功状态只读取 IsSuccess，其他字段随结果完整保存。</summary>
    internal static PuloniaTaskOutcome CreateOutcome(PathingResult result, string? resourceVersion)
    {
        var data = JObject.FromObject(result);
        data["resource_version"] = resourceVersion;
        return result.IsSuccess
            ? PuloniaTaskOutcome.Success("地图追踪文件完整执行成功，按整份文件执行成功估算采集完成。", data)
            : PuloniaTaskOutcome.Failure(result.ErrorMessage ?? "地图追踪路线未完整执行。", data);
    }
}
