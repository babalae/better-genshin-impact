using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Service.Mcp;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddBetterGiMcp(this IServiceCollection services)
    {
        var viewModelTypes = services
            .Select(x => x.ServiceType)
            .Where(x => typeof(IViewModel).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface)
            .Distinct()
            .ToArray();

        services.AddSingleton(new McpCommandCatalogOptions(viewModelTypes));
        services.AddSingleton<McpCommandCatalog>();
        services.AddSingleton<McpDetachedTaskRegistry>();
        services.AddHostedService<McpHostedService>();
        return services;
    }
}

public sealed record McpCommandCatalogOptions(IReadOnlyList<Type> ViewModelTypes);
