using Microsoft.Extensions.DependencyInjection;

namespace MicaSetup.Services;

#pragma warning disable CS8618

public class ServiceManager
{
    public static ServiceProvider Services { get; set; }

    public static T GetService<T>()
    {
        return Services.GetService<T>()!;
    }
}
