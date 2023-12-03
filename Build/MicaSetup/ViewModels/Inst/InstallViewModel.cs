using CommunityToolkit.Mvvm.ComponentModel;
using MicaSetup.Design.Controls;
using MicaSetup.Helper;
using MicaSetup.Helper.Helper;
using MicaSetup.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MicaSetup.ViewModels;

#pragma warning disable CS0162
#pragma warning disable IDE0035

public partial class InstallViewModel : ObservableObject
{
    [ObservableProperty]
    private string installInfo = string.Empty;

    [ObservableProperty]
    private double installProgress = 0d;

    public InstallViewModel()
    {
        Option.Current.Installing = true;
        InstallInfo = Mui("Preparing");

        _ = Task.Run(async () =>
        {
            await Task.Delay(200).ConfigureAwait(true);

            try
            {
                using Stream archiveStream = ResourceHelper.GetStream("pack://application:,,,/MicaSetup;component/Resources/Setups/publish.7z");
                InstallInfo = Mui("ProgressTipsInstalling");
                InstallHelper.Install(archiveStream, (progress, key) =>
                {
                    UIDispatcherHelper.BeginInvoke(() =>
                    {
                        InstallProgress = progress * 100d;
                        InstallInfo = key;
                    });
                });

                using Stream uninstStream = ResourceHelper.GetStream("pack://application:,,,/MicaSetup;component/Resources/Setups/Uninst.exe");
                InstallHelper.CreateUninst(uninstStream);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            if (Option.Current.IsAllowFullFolderSecurity)
            {
                try
                {
                    SecurityControlHelper.AllowFullFolderSecurity(Option.Current.InstallLocation);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            if (Option.Current.IsAllowFirewall)
            {
                try
                {
                    FirewallHelper.AllowApplication(Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName));
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            if (false)
            {
                try
                {
                    IDotNetVersionService dotNetService = ServiceManager.GetService<IDotNetVersionService>();
                    DotNetInstallInfo info = dotNetService.GetInfo(new Version(4, 8));

                    try
                    {
                        if (dotNetService.GetNetFrameworkVersion() < info.Version)
                        {
                            InstallInfo = $"{Mui("Preparing")} {info.Name}";
                            if (!dotNetService.InstallNetFramework(info.Version, (t, e) =>
                            {
                                UIDispatcherHelper.BeginInvoke(() =>
                                {
                                    InstallInfo = $"{t switch { ProgressType.Download => Mui("Downloading"), _ or ProgressType.Install => Mui("Installing") }} {info.Name}";
                                    InstallProgress = e.ProgressPercentage;
                                });
                            }))
                            {
                                UIDispatcherHelper.BeginInvoke(() =>
                                {
                                    _ = MessageBoxX.Info(null!, Mui("ComponentInstallFailedTips", info.Name));
                                    _ = FluentProcess.Start("explorer.exe", info.ThankYouUrl);
                                });
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                        UIDispatcherHelper.BeginInvoke(() =>
                        {
                            _ = MessageBoxX.Info(null!, Mui("ComponentInstallFailedTips", info.Name) + Environment.NewLine + e.Message);
                            _ = FluentProcess.Start("explorer.exe", info.ThankYouUrl);
                        });
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            InstallInfo = Mui("InstallFinishTips");
            Option.Current.Installing = false;
            await Task.Delay(200).ConfigureAwait(false);

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
        });
    }
}
