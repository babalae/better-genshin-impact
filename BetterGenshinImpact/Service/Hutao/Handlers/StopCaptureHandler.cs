using System;
using System.IO.Pipes;
using System.Text.Json;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Service.Hutao.Handlers;

internal sealed class StopCaptureHandler : IPipeRequestHandler
{
    private readonly IServiceProvider serviceProvider;

    public StopCaptureHandler(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public bool CanHandle(PipeRequestKind kind) => kind == PipeRequestKind.StopCapture;

    public void HandleRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        HomePageViewModel home = serviceProvider.GetRequiredService<HomePageViewModel>();
        home.StopCaptureFromHutao();
        stream.WriteResponse(new PipeResponse<bool> { Kind = PipeResponseKind.Boolean, Data = true });
    }
}
