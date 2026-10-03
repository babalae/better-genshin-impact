using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Pulonia.Models;
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
        PublicParameters = ["with_delay"]
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("录制回放节点没有固定资源路径。");
        var json = await File.ReadAllTextAsync(task.Path, ct).ConfigureAwait(false);
        await KeyMouseMacroPlayer.PlayMacro(json, ct, task.Parameters.Value<bool>("with_delay")).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PuloniaTaskOutcome.ExecutedUnverified("键鼠宏已完整回放，但录制文件没有结构化副作用证据。",
            new JObject { ["resource_version"] = task.ResourceVersion });
    }
}
