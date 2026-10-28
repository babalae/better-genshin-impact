using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
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
    /// <summary>
    /// 注入的配置根，传给路线执行器。
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 使用配置根建立执行器。
    /// </summary>
    /// <param name="config">注入的配置根</param>
    public PuloniaPathingTaskExecutor(AllConfig config)
    {
        _config = config;
    }

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
        // 地图追踪使用全部宿主共享设置和路线 CD；这些参数只能经共享设置修改。
        SettingBindings = PuloniaSettingCatalog.CreateBindings(
            [.. PuloniaSettingCatalog.HostCategories, PuloniaSettingCatalog.CollectionCategory])
    }];

    /// <summary>把路线 CD 加入地图追踪 Schema；Schema 本身只由 PuloniaCollectionCooldown.CreateSchema 一处定义。</summary>
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
        if (task.Resource is null && string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("地图追踪节点没有固定资源。");
        // 路线只使用本节点租约记录的运行环境；它已停止时当前节点失败
        if (!context.TryGetRuntime(out var runtime, out var runtimeError))
            return PuloniaTaskOutcome.Failure(runtimeError);
        var pathingTask = task.Resource is { } resource
            ? await ReadRepositoryRouteAsync(resource, ct).ConfigureAwait(false)
            : PathingTask.BuildFromFilePath(task.Path!);
        if (pathingTask is null)
            return PuloniaTaskOutcome.Failure("路线版本高于当前 BetterGI，已拒绝执行。");

        var parameters = task.Parameters;
        var partyConfig = PuloniaTaskCommonSettings.FromParameters("pathing", parameters).PathingConfig;
        // 触发器租约仅属于当前节点，异常或取消时也会撤销。
        using var autoPick = partyConfig.AutoPickEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null) : null;
        using var autoEat = partyConfig.AutoEatEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoEat", null) : null;

        var executor = new PathExecutor(runtime, _config, ct) { PartyConfig = partyConfig };
        var result = await executor.Pathing(pathingTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return CreateOutcome(result, task.ResourceVersion);
    }

    /// <summary>
    /// 从已保留归档读取仓库路线正文，不落地缓存文件。
    /// 逻辑路径取 User\AutoPathing 下对应的位置，只用于文件名、日志和按目录识别采集物。
    /// </summary>
    private static async Task<PathingTask?> ReadRepositoryRouteAsync(ScriptResourceReference resource, CancellationToken ct)
    {
        string json;
        using (var snapshot = await ScriptRepositoryStore.Shared.OpenApprovedAsync(resource, ct).ConfigureAwait(false))
            json = snapshot.ReadText("repo/" + resource.RelativePath);
        const string pathingPrefix = "pathing/";
        var relative = resource.RelativePath.StartsWith(pathingPrefix, StringComparison.OrdinalIgnoreCase)
            ? resource.RelativePath[pathingPrefix.Length..] : resource.RelativePath;
        var logicalPath = Path.Combine(MapPathingViewModel.PathJsonPath, relative.Replace('/', Path.DirectorySeparatorChar));
        return PathingTask.BuildFromJson(json, logicalPath);
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
