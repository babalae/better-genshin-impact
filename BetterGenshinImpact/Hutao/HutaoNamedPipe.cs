using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;

namespace BetterGenshinImpact.Hutao;

internal sealed class HutaoNamedPipe : IDisposable
{
    private readonly NamedPipeClientStream clientStream = new(".", "Snap.Hutao.PrivateNamedPipe", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);

    private readonly IServiceProvider serviceProvider;

    private Lazy<bool> isSupported;

    public HutaoNamedPipe(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;

        isSupported = new(() =>
        {
            try
            {
                clientStream.Connect(TimeSpan.Zero);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        });
    }

    public bool IsSupported
    {
        get => isSupported.Value;
    }

    public void Dispose()
    {
        clientStream.Dispose();
    }
}