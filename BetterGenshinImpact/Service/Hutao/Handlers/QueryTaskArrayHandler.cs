using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Model.Hutao;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal sealed class QueryTaskArrayHandler : IPipeRequestHandler
{
    public bool CanHandle(PipeRequestKind kind) => kind == PipeRequestKind.QueryTaskArray;

    public void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        stream.WriteResponse(new PipeResponse<AutomationTaskDefinition[]> { Kind = PipeResponseKind.Array, Data = QueryTaskArray() });
    }

    private static AutomationTaskDefinition[] QueryTaskArray()
    {
        if (GameTaskManager.TriggerDictionary is not { } triggers)
        {
            return [];
        }

        return triggers
            .Select(kv => new AutomationTaskDefinition
            {
                Id = kv.Key,
                Name = kv.Value.Name,
                // ITaskTrigger 没有描述信息,而契约 DTO 要求该字段,先占位为空串。
                Description = string.Empty,
            })
            .ToArray();
    }
}
