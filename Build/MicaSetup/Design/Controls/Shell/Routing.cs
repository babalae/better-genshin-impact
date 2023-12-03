using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MicaSetup.Design.Controls;

public static class Routing
{
    public static ServiceProvider Provider { get; internal set; } = null!;
    public static WeakReference<ShellControl> Shell { get; internal set; } = null!;

    public static void RegisterRoute()
    {
        ServiceCollection serviceCollection = new();

        foreach (var pageItem in ShellPageSetting.PageDict)
        {
            serviceCollection.Register(pageItem.Key, pageItem.Value);
        }
        Provider = serviceCollection.BuildServiceProvider();
    }

    public static FrameworkElement ResolveRoute(string route)
    {
        if (string.IsNullOrEmpty(route))
        {
            return null!;
        }
        return Provider?.Resolve<FrameworkElement>(route)!;
    }

    public static void GoTo(string route)
    {
        if (Shell != null)
        {
            if (Shell.TryGetTarget(out ShellControl shell))
            {
                OnGoTo(route);
                shell.Content = ResolveRoute(route);
                shell.Route = route;
            }
        }
    }

    public static void GoToNext()
    {
        if (Shell != null)
        {
            if (Shell.TryGetTarget(out ShellControl shell))
            {
                if (ShellPageSetting.PageDict.ContainsKey(shell.Route))
                {
                    bool found = false;
                    foreach (var item in ShellPageSetting.PageDict)
                    {
                        if (found)
                        {
                            OnGoTo(item.Key);
                            shell.Content = ResolveRoute(item.Key);
                            shell.Route = item.Key;
                            break;
                        }
                        if (item.Key == shell.Route)
                        {
                            found = true;
                        }
                    }
                }
            }
        }
    }

    private static void OnGoTo(string route)
    {
    }
}

file class RoutingServiceInfo
{
    public string Name { get; set; }
    public Type Type { get; set; }

    public RoutingServiceInfo(string name, Type type)
    {
        Name = name;
        Type = type;
    }
}

file static class RoutingExtension
{
    public static IServiceCollection Register(this IServiceCollection services, string name, Type type)
    {
        services.AddSingleton(type);
        services.AddSingleton(new RoutingServiceInfo(name, type));
        return services;
    }

    public static T Resolve<T>(this IServiceProvider serviceProvider, string name)
    {
        var serviceInfo = serviceProvider.GetRequiredService<IEnumerable<RoutingServiceInfo>>()
            .FirstOrDefault(x => x.Name == name);

        if (serviceInfo == null)
        {
            throw new InvalidOperationException($"Service '{name}' not found");
        }
        return (T)serviceProvider.GetRequiredService(serviceInfo.Type);
    }
}
