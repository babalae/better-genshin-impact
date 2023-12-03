using CommunityToolkit.Mvvm.ComponentModel;
using MicaSetup.Design.Controls;
using MicaSetup.Helper;
using MicaSetup.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MicaSetup.ViewModels;

public partial class UninstallViewModel : ObservableObject
{
    [ObservableProperty]
    private string installInfo = string.Empty;

    [ObservableProperty]
    private double installProgress = 0d;

    public UninstallViewModel()
    {
        Option.Current.Uninstalling = true;
        InstallInfo = Mui("Preparing");

        Task.Run(async () =>
        {
            await Task.Delay(200);
            InstallInfo = Mui("ProgressTipsUninstalling");

            UninstallHelper.Uninstall((progress, key) =>
            {
                UIDispatcherHelper.BeginInvoke(() =>
                {
                    InstallProgress = progress * 100d;
                    InstallInfo = key;
                });
            }, (report, _) =>
            {
                if (report == UninstallReport.AnyDeleteDelayUntilReboot)
                {
                    UIDispatcherHelper.Invoke(main =>
                    {
                        _ = MessageBoxX.Info(main, Mui("UninstallDelayUntilRebootTips"));
                    });
                }
            });

            if (Option.Current.IsAllowFirewall)
            {
                try
                {
                    FirewallHelper.RemoveApplication(Path.Combine(Option.Current.InstallLocation ?? string.Empty, Option.Current.ExeName));
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            Option.Current.Uninstalling = false;
            await Task.Delay(200);

            if (Option.Current.IsRefreshExplorer)
            {
                try
                {
                    ServiceManager.GetService<IExplorerService>().Refresh();
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }
            UIDispatcherHelper.Invoke(Routing.GoToNext);
        }).Forget();
    }
}
