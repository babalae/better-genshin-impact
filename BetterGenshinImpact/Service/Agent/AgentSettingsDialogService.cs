using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace BetterGenshinImpact.Service.Agent;

public sealed class AgentSettingsDialogService(IServiceProvider services)
{
    public void Show()
    {
        var window = ActivatorUtilities.CreateInstance<AgentSettingsWindow>(services);
        window.Owner = Application.Current.MainWindow;
        _ = window.ShowDialog();
    }
}
