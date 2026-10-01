using System.IO.Pipes;
using System.Text.Json;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal interface IPipeRequestHandler
{
    bool CanHandle(PipeRequestKind kind);

    void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request);
}
