using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace BetterGenshinImpact.Helpers.Extensions;

internal static class DependencyInjectionExtensions
{
    public static IServiceCollection AddView<TWindow, TWindowImplementation, TViewModel>(this IServiceCollection services)
        where TWindow : class
        where TWindowImplementation : class, TWindow
        where TViewModel : class, IViewModel
    {
        return services
            .AddSingleton<TWindow, TWindowImplementation>()
            .AddSingleton<TViewModel>();
    }

    public static IServiceCollection AddView<TPage, TViewModel>(this IServiceCollection services)
        where TPage : Page
        where TViewModel : class, IViewModel
    {
        return services
            .AddSingleton<TPage>()
            .AddSingleton<TViewModel>();
    }
}