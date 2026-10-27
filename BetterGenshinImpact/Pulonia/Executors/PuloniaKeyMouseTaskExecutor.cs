using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 使用现有录制回放器执行 Pulonia 键鼠宏资源。
/// </summary>
public sealed class PuloniaKeyMouseTaskExecutor : IPuloniaTaskExecutor
{

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "keymouse",
        DisplayName = "录制回放",
        Description = "选择本地键鼠录制文件，并配置是否按录制延时回放。",
        RequiresGameSession = true,
        ResourceBaseDirectory = Global.Absolute(@"User\KeyMouseScript"),
        DefaultParameters = new JObject { ["with_delay"] = false },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["with_delay"] = new JObject { ["type"] = "boolean" }
            },
            ["additionalProperties"] = false
        },
        // 回放方式经共享设置继承，录制目录引用也能统一设置。
        SettingBindings = PuloniaSettingCatalog.CreateBindings(PuloniaSettingCatalog.KeyMouseCategory)
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (task.Resource is null && string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("录制回放节点没有固定资源。");
        // 回放只使用本节点租约记录的运行环境；它已停止时当前节点失败
        if (!context.TryGetRuntime(out var runtime, out var runtimeError))
            return PuloniaTaskOutcome.Failure(runtimeError);
        string json;
        if (task.Resource is { } resource)
        {
            // 仓库录制从已保留归档读取正文，不落地缓存文件。
            using var snapshot = await ScriptRepositoryStore.Shared.OpenApprovedAsync(resource, ct).ConfigureAwait(false);
            json = snapshot.ReadText("repo/" + resource.RelativePath);
        }
        else
        {
            json = await File.ReadAllTextAsync(task.Path!, ct).ConfigureAwait(false);
        }
        await KeyMouseMacroPlayer.PlayMacro(runtime, json, ct, task.Parameters.Value<bool>("with_delay")).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PuloniaTaskOutcome.ExecutedUnverified("键鼠宏已完整回放，但录制文件没有结构化副作用证据。",
            new JObject { ["resource_version"] = task.ResourceVersion });
    }
}
