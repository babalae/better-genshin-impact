using System;
using System.IO.Pipes;
using System.Text.Json;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal sealed class StartCaptureHandler : IPipeRequestHandler
{
    private readonly IServiceProvider serviceProvider;

    public StartCaptureHandler(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public bool CanHandle(PipeRequestKind kind) => kind == PipeRequestKind.StartCapture;

    public void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        HomePageViewModel home = serviceProvider.GetRequiredService<HomePageViewModel>();
        home.StartCaptureFromHutao((nint)request.Data.GetInt64());
        stream.WriteResponse(new PipeResponse<bool> { Kind = PipeResponseKind.Boolean, Data = true });
    }
}
