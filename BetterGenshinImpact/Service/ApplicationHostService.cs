using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Managed host of the application.
/// </summary>
public class ApplicationHostService(
    IServiceProvider serviceProvider,
    InstanceService instanceService) : IHostedService
{
    private INavigationWindow? _navigationWindow;
    private readonly ILogger<ApplicationHostService> _logger = App.GetLogger<ApplicationHostService>();

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
        instanceService.MarkApplicationReady();
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = (serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
            _navigationWindow!.ShowWindow();

            var cmdOptions = CommandLineOptions.Instance;

            if (cmdOptions.HasTaskArgs)
            {
                //无论如何，先跳到主页，否则在通过参数的任务在执行完之前，不会加载快捷键
                _ = _navigationWindow.Navigate(typeof(HomePage));

                // 命令行启动时，并行更新订阅脚本（不阻塞游戏启动和导航）
                // StartGameTask 会在游戏进入主界面后等待此 Task 完成，再开始执行任务
                var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
                if (scriptConfig.AutoUpdateBeforeCommandLineRun)
                {
                    ScriptRepoUpdater.Instance.CommandLineAutoUpdateTask =
                        Task.Run(() => ScriptRepoUpdater.Instance.AutoUpdateSubscribedScripts());
                }

                switch (cmdOptions.Action)
                {
                    case CommandLineAction.StartOneDragon:
                        // 通过命令行参数启动「一条龙」 => 跳转到一条龙配置页。
                        _ = _navigationWindow.Navigate(typeof(OneDragonFlowPage));
                        var oneDragon = App.GetService<OneDragonFlowViewModel>();
                        if (oneDragon != null)
                        {
                            _ = ObserveCommandLineTaskAsync(
                                oneDragon.RunCommandLineAsync(cmdOptions.OneDragonConfigName),
                                "一条龙");
                        }
                        break;

                    case CommandLineAction.StartGroups:
                        // 通过命令行参数启动「调度组」 => 跳转到调度器配置页。
                        _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                        var scriptGroupScheduler = App.GetService<ScriptControlViewModel>();
                        if (scriptGroupScheduler != null)
                        {
                            _ = ObserveCommandLineTaskAsync(
                                scriptGroupScheduler.OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames),
                                "配置组");
                        }
                        break;

                    case CommandLineAction.TaskProgress:
                        // 通过命令行参数启动「任务进度」 => 跳转到调度器配置页。
                        _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                        var taskProgressScheduler = App.GetService<ScriptControlViewModel>();
                        if (taskProgressScheduler != null)
                        {
                            _ = ObserveCommandLineTaskAsync(
                                taskProgressScheduler.OnStartMultiScriptTaskProgressAsync(cmdOptions.GroupNames),
                                "任务进度");
                        }
                        break;

                    case CommandLineAction.Start:
                        // 通过命令行参数打开「启动页开关」 => 跳转到主页。
                        _ = _navigationWindow.Navigate(typeof(HomePage));
                        // 后续代码在 HomePageViewModel / OnLoaded 中。
                        break;
                }
            }
            else
            {
                // 通过双击程序启动 => 跳转到主页。
                _ = _navigationWindow.Navigate(typeof(HomePage));
            }
        }
        //
        await Task.CompletedTask;
    }

    private async Task ObserveCommandLineTaskAsync(Task task, string taskName)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogInformation(exception, "命令行{TaskName}已取消", taskName);
        }
        catch (NormalEndException exception)
        {
            _logger.LogInformation(exception, "命令行{TaskName}已正常结束", taskName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "命令行{TaskName}执行失败", taskName);
            CommandLineTaskFailurePolicy.MarkFailed(exitCode => Environment.ExitCode = exitCode);
        }
    }
}

internal static class CommandLineTaskFailurePolicy
{
    internal const int FailureExitCode = 1;

    internal static void MarkFailed(Action<int> setExitCode)
    {
        setExitCode(FailureExitCode);
    }
}
