using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BetterGenshinImpact.Hutao;

internal sealed partial class HutaoNamedPipe : IDisposable
{
    private const int Version = 1;

    private readonly NamedPipeClientStream clientStream = new(".", "Snap.Hutao.PrivateNamedPipe", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);

    private readonly IServiceProvider serviceProvider;

    private readonly Lazy<bool> isSupported;

    public HutaoNamedPipe(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;

        isSupported = new(clientStream.TryConnectOnce);
    }

    public bool IsSupported
    {
        get => isSupported.Value;
    }

    public bool TryRedirectLog(string log)
    {
        if (!IsSupported)
        {
            return false;
        }

        if (!clientStream.TryConnectOnce())
        {
            return false;
        }

        try
        {
            PipeRequest<string> logRequest = new() { Kind = PipeRequestKind.Log, Data = log, };
            clientStream.WritePacketWithJsonContent(Version, PipePacketType.Request, PipePacketCommand.BetterGenshinImpactToSnapHutaoRequest, logRequest);
            clientStream.ReadPacket(out _, out PipeResponse<JsonElement>? _);
            return true;
        }
        finally
        {
            clientStream.WritePacket(Version, PipePacketType.SessionTermination, PipePacketCommand.None);
            clientStream.Flush();
        }
    }

    public void Dispose()
    {
        clientStream.Dispose();
    }
}