using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal sealed class StartTaskHandler : IPipeRequestHandler
{
    public bool CanHandle(PipeRequestKind kind) => kind == PipeRequestKind.StartTask;

    public void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        string? taskId = request.Data.ValueKind == JsonValueKind.String ? request.Data.GetString() : null;
        stream.WriteResponse(new PipeResponse<bool> { Kind = PipeResponseKind.Boolean, Data = TryStartTask(taskId) });
    }

    private static bool TryStartTask(string? id)
    {
        if (string.IsNullOrEmpty(id) || GameTaskManager.TriggerDictionary is not { } triggers)
        {
            return false;
        }

        if (triggers.TryGetValue(id, out ITaskTrigger? trigger))
        {
            trigger.IsEnabled = true;
            return true;
        }

        trigger = triggers.Values.FirstOrDefault(t => t.Name == id);
        if (trigger is null)
        {
            return false;
        }

        trigger.IsEnabled = true;
        return true;
    }
}
