using System.IO.Pipes;
using System.Text.Json;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal sealed class GetContractVersionHandler : IPipeRequestHandler
{
    public bool CanHandle(PipeRequestKind kind) => kind == PipeRequestKind.GetContractVersion;

    public void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        stream.WriteResponse(new PipeResponse<uint> { Kind = PipeResponseKind.Number, Data = HutaoPipeProtocol.Version });
    }
}
