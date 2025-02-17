using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Hutao;

internal sealed partial class BGINamedPipe : IDisposable
{
    private const int Version = 1;

    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<BGINamedPipe> logger;

    private readonly CancellationTokenSource serverTokenSource = new();
    private readonly TaskCompletionSource serverRunTaskCompletionSource = new();

    private readonly NamedPipeServerStream serverStream;

    public BGINamedPipe(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        logger = serviceProvider.GetRequiredService<ILogger<BGINamedPipe>>();

        PipeSecurity? pipeSecurity = default;

        if (RuntimeHelper.IsElevated)
        {
            SecurityIdentifier everyOne = new(WellKnownSidType.WorldSid, null);

            pipeSecurity = new();
            pipeSecurity.AddAccessRule(new PipeAccessRule(everyOne, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        serverStream = NamedPipeServerStreamAcl.Create(
            "BetterGenshinImpact.NamedPipe",
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            0,
            0,
            pipeSecurity);
    }

    public void Dispose()
    {
        serverTokenSource.Cancel();
        serverRunTaskCompletionSource.Task.GetAwaiter().GetResult();
        serverTokenSource.Dispose();
        serverStream.Dispose();
    }

    public async ValueTask RunAsync()
    {
        while (!serverTokenSource.IsCancellationRequested)
        {
            try
            {
                await serverStream.WaitForConnectionAsync(serverTokenSource.Token).ConfigureAwait(false);
                logger.LogInformation("Pipe session created");
                RunPacketSession(serverStream, serverTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        serverRunTaskCompletionSource.TrySetResult();
    }

    private void RunPacketSession(NamedPipeServerStream serverStream, CancellationToken token)
    {
        while (serverStream.IsConnected && !token.IsCancellationRequested)
        {
            serverStream.ReadPacket(out PipePacketHeader header);
            logger.LogInformation("Pipe packet: [Type:{Type}] [Command:{Command}]", header.Type, header.Command);
            switch ((header.Type, header.Command))
            {
                case (PipePacketType.Request, PipePacketCommand.SnapHutaoToBetterGenshinImpactRequest):
                    if (serverStream.ReadJsonContent<PipeRequest<JsonElement>>(in header) is { } request)
                    {
                        DispatchHutaoRequest(request);
                    }

                    break;

                case (PipePacketType.SessionTermination, _):
                    serverStream.Disconnect();
                    return;
            }
        }
    }

    private void DispatchHutaoRequest(PipeRequest<JsonElement> request)
    {
        switch (request.Kind)
        {
            case PipeRequestKind.GetContractVersion:
                {
                    PipeResponse<uint> response = new() { Kind = PipeResponseKind.Number, Data = Version };
                    serverStream.WritePacketWithJsonContent(Version, PipePacketType.Response, PipePacketCommand.BetterGenshinImpactToSnapHutaoResponse, response);
                    serverStream.Flush();
                }

                break;

            case PipeRequestKind.StartCapture:
                {
                    HomePageViewModel home = serviceProvider.GetRequiredService<HomePageViewModel>();
                    home.Start((nint)request.Data.GetInt64());
                }

                break;

            case PipeRequestKind.StopCapture:
                {
                    HomePageViewModel home = serviceProvider.GetRequiredService<HomePageViewModel>();
                    home.Stop();
                }

                break;

            case PipeRequestKind.QueryTaskArray:
                throw new NotImplementedException();

            case PipeRequestKind.StartTask:
                throw new NotImplementedException();

            case PipeRequestKind.EndSwitchToNextGameAccount:
                throw new NotImplementedException();
        }
    }
}