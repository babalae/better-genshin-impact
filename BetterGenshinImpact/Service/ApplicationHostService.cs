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
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.DependencyInjection;
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
    /// 网页版实例不创建主界面，云原神宿主窗口就是唯一的界面（由 WebPageRuntimeProvider 在启动截图器时打开）。
    /// 页面的 Loaded / 导航不会发生，命令行任务直接交给对应的 ViewModel
    /// </summary>
    private void HandleWebViewActivation(CommandLineOptions cmdOptions)
    {
        // 总是先启动截图器：打开宿主并等待进入游戏，HomePageViewModel 同时负责遮罩窗口与键鼠监听。
        // 宿主在 StartAsync 的同步部分就已创建并设为主窗口，后面的命令行任务弹出的提示因此有 Owner；
        // 任务自身调用 StartGameTask 时会等这次启动完成
        serviceProvider.GetRequiredService<HomePageViewModel>().HandleActivation(cmdOptions);

        switch (cmdOptions.Action)
        {
            case CommandLineAction.StartOneDragon:
                // 没有页面：模拟一条龙页面的导航（加载配置列表）与首次加载（按命令行选配置并执行）
                var oneDragon = serviceProvider.GetRequiredService<OneDragonFlowViewModel>();
                oneDragon.OnNavigatedTo();
                oneDragon.LoadedCommand.Execute(null);
                break;

            case CommandLineAction.StartGroups when cmdOptions.GroupNames.Length > 0:
                _ = serviceProvider.GetRequiredService<ScriptControlViewModel>()
                    .OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames);
                break;

            case CommandLineAction.TaskProgress when cmdOptions.GroupNames.Length > 0:
                _ = serviceProvider.GetRequiredService<ScriptControlViewModel>()
                    .OnStartMultiScriptTaskProgressAsync(cmdOptions.GroupNames);
                break;
        }
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        if (instanceService.Context.IsWebView)
        {
            HandleWebViewActivation(CommandLineOptions.Instance);
            return;
        }

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
                if (instanceService.Context.IsRoot && scriptConfig.AutoUpdateBeforeCommandLineRun)
                {
                    ScriptRepoUpdater.Instance.CommandLineAutoUpdateTask =
                        Task.Run(() => ScriptRepoUpdater.Instance.AutoUpdateSubscribedScripts());
                }

                switch (cmdOptions.Action)
                {
                    case CommandLineAction.StartOneDragon:
                        // 通过命令行参数启动「一条龙」 => 跳转到一条龙配置页。
                        _ = _navigationWindow.Navigate(typeof(OneDragonFlowPage));
                        // 后续代码在 OneDragonFlowViewModel / OnLoaded 中。
                        break;

                    case CommandLineAction.StartGroups:
                        // 通过命令行参数启动「调度组」 => 跳转到调度器配置页。
                        _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                        if (cmdOptions.GroupNames.Length > 0)
                        {
                            var scheduler = App.GetService<ScriptControlViewModel>();
                            scheduler?.OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames);
                        }
                        break;

                    case CommandLineAction.TaskProgress:
                        // 通过命令行参数启动「任务进度」 => 跳转到调度器配置页。
                        _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                        if (cmdOptions.GroupNames.Length > 0)
                        {
                            var scheduler = App.GetService<ScriptControlViewModel>();
                            scheduler?.OnStartMultiScriptTaskProgressAsync(cmdOptions.GroupNames);
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
}
