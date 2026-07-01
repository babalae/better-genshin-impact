using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Managed host of the application.
/// </summary>
public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
{
    private readonly ILogger<ApplicationHostService> _logger = App.GetLogger<ApplicationHostService>();
    private INavigationWindow? _navigationWindow;

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RuntimeHelper.SingleInstanceActivated += OnSingleInstanceActivated;
        await HandleActivationAsync(CommandLineOptions.Instance, false);
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        RuntimeHelper.SingleInstanceActivated -= OnSingleInstanceActivated;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles first launch with the original page-loaded flow, and subsequent single-instance activations explicitly.
    /// </summary>
    private async Task HandleActivationAsync(CommandLineOptions cmdOptions, bool isSingleInstanceActivation)
    {
        EnsureNavigationWindow();

        if (isSingleInstanceActivation)
        {
            QueueActivationAction(async () => await RunSingleInstanceActivationAsync(cmdOptions));
        }
        else
        {
            QueueActivationAction(async () => await RunFirstLaunchActivationAsync(cmdOptions));
        }

        await Task.CompletedTask;
    }

    private async Task RunFirstLaunchActivationAsync(CommandLineOptions cmdOptions)
    {
        if (cmdOptions.HasTaskArgs)
        {
            _ = _navigationWindow!.Navigate(typeof(HomePage));

            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
            if (scriptConfig.AutoUpdateBeforeCommandLineRun)
            {
                ScriptRepoUpdater.Instance.CommandLineAutoUpdateTask =
                    Task.Run(() => ScriptRepoUpdater.Instance.AutoUpdateSubscribedScripts());
            }

            switch (cmdOptions.Action)
            {
                case CommandLineAction.StartOneDragon:
                    _ = _navigationWindow.Navigate(typeof(OneDragonFlowPage));
                    break;

                case CommandLineAction.StartGroups:
                    _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                    if (cmdOptions.GroupNames.Length > 0)
                    {
                        var scheduler = App.GetService<ScriptControlViewModel>();
                        if (scheduler != null)
                        {
                            await scheduler.OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames);
                        }
                    }
                    break;

                case CommandLineAction.TaskProgress:
                    _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                    if (cmdOptions.GroupNames.Length > 0)
                    {
                        var scheduler = App.GetService<ScriptControlViewModel>();
                        if (scheduler != null)
                        {
                            await scheduler.OnStartMultiScriptTaskProgressAsync(cmdOptions.GroupNames);
                        }
                    }
                    break;

                case CommandLineAction.Start:
                    _ = _navigationWindow.Navigate(typeof(HomePage));
                    break;
            }
        }
        else
        {
            _ = _navigationWindow!.Navigate(typeof(HomePage));
        }
    }

    private async Task RunSingleInstanceActivationAsync(CommandLineOptions cmdOptions)
    {
        if (!cmdOptions.HasTaskArgs)
        {
            return;
        }

        _ = _navigationWindow!.Navigate(typeof(HomePage));

        var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
        if (scriptConfig.AutoUpdateBeforeCommandLineRun)
        {
            ScriptRepoUpdater.Instance.CommandLineAutoUpdateTask =
                Task.Run(() => ScriptRepoUpdater.Instance.AutoUpdateSubscribedScripts());
        }

        switch (cmdOptions.Action)
        {
            case CommandLineAction.StartOneDragon:
                _ = _navigationWindow.Navigate(typeof(OneDragonFlowPage));
                OneDragonFlowViewModel? oneDragon = App.GetService<OneDragonFlowViewModel>();
                if (oneDragon != null)
                {
                    await oneDragon.StartFromCommandLineOptionsAsync(cmdOptions);
                }
                break;

            case CommandLineAction.StartGroups:
                _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                if (cmdOptions.GroupNames.Length > 0)
                {
                    var scheduler = App.GetService<ScriptControlViewModel>();
                    if (scheduler != null)
                    {
                        await scheduler.OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames);
                    }
                }
                break;

            case CommandLineAction.TaskProgress:
                _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                if (cmdOptions.GroupNames.Length > 0)
                {
                    var scheduler = App.GetService<ScriptControlViewModel>();
                    if (scheduler != null)
                    {
                        await scheduler.OnStartMultiScriptTaskProgressAsync(cmdOptions.GroupNames);
                    }
                }
                break;

            case CommandLineAction.Start:
                _ = _navigationWindow.Navigate(typeof(HomePage));
                HomePageViewModel? home = App.GetService<HomePageViewModel>();
                if (home != null)
                {
                    await home.OnStartTriggerAsync();
                }
                break;
        }
    }
    private void EnsureNavigationWindow()
    {
        _navigationWindow ??= (serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;

        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow.ShowWindow();
        }
    }

    private void OnSingleInstanceActivated(object? sender, SingleInstanceActivatedEventArgs e)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await HandleActivationAsync(CommandLineOptions.ParseActivationArgs(e.Args), true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Handle single instance activation failed");
            }
        });
    }

    private void QueueActivationAction(Func<Task> action)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Run command line activation action failed");
            }
        }, DispatcherPriority.ApplicationIdle);
    }
}
