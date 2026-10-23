using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Managed host of the application.
/// </summary>
/// <param name="config">注入的配置根，命令行启动时读取脚本订阅的自动更新开关</param>
public class ApplicationHostService(
    IServiceProvider serviceProvider,
    InstanceService instanceService,
    IInferenceModelDiagnostics inferenceDiagnostics,
    AllConfig config) : IHostedService
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
    /// 云原神子实例显示与桌面分身一致的 BetterGI 主界面，同时在后台创建固定 1080P 的网页宿主。
    /// 命令行任务仍直接交给对应 ViewModel，避免主界面导航时序影响自动启动。
    /// </summary>
    private void HandleWebViewActivation(CommandLineOptions cmdOptions)
    {
        // 总是先启动截图器：创建后台 1080P 宿主并等待进入游戏，HomePageViewModel 同时负责遮罩窗口与键鼠监听。
        // BetterGI 主窗口已经创建，后面的命令行任务弹窗会自然以它为 Owner；任务自身经 TaskRunner
        // 获取运行环境时会等待这次启动完成。
        serviceProvider.GetRequiredService<HomePageViewModel>().HandleActivation(cmdOptions);

        switch (cmdOptions.Action)
        {
            case CommandLineAction.StartOneDragon:
                // 已有主界面后走正常导航，让页面按原有生命周期加载配置并执行。
                _ = _navigationWindow?.Navigate(typeof(OneDragonFlowPage));
                break;

            case CommandLineAction.StartGroups when cmdOptions.GroupNames.Length > 0:
                _ = _navigationWindow?.Navigate(typeof(ScriptControlPage));
                _ = serviceProvider.GetRequiredService<ScriptControlViewModel>()
                    .OnStartMultiScriptGroupWithNamesAsync(cmdOptions.GroupNames);
                break;

        }
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = (serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
            if (_navigationWindow is Window window) Application.Current.MainWindow = window;
            _navigationWindow!.ShowWindow();

            var cmdOptions = CommandLineOptions.Instance;
            if (instanceService.Context.IsWebView)
            {
                // 先展示首页，避免 OCR 预热期间出现无界面进程；预热失败不阻止用户登录云原神。
                _ = _navigationWindow.Navigate(typeof(HomePage));
                try
                {
                    await inferenceDiagnostics.PreheatOcrAsync();
                }
                catch (Exception e)
                {
                    await BetterGenshinImpact.View.Windows.ThemedMessageBox.WarningAsync($"OCR 预热失败：{e.Message}");
                }

                HandleWebViewActivation(cmdOptions);
                return;
            }

            if (cmdOptions.IsPuloniaAction)
            {
                _ = _navigationWindow.Navigate(typeof(PuloniaTaskHistoryPage));
                await serviceProvider.GetRequiredService<Pulonia.Services.PuloniaTaskTriggerHost>().HandleActivationAsync(cmdOptions);
                return;
            }

            if (cmdOptions.HasTaskArgs)
            {
                //无论如何，先跳到主页，否则在通过参数的任务在执行完之前，不会加载快捷键
                _ = _navigationWindow.Navigate(typeof(HomePage));

                // 命令行启动时，并行更新订阅脚本（不阻塞游戏启动和导航）
                // TaskRunner 获取运行环境后会等待这次更新完成，再开始执行任务
                var scriptConfig = config.ScriptConfig;
                if (instanceService.Context.IsRoot && scriptConfig.AutoUpdateBeforeCommandLineRun)
                {
                    ScriptRepoUpdater.Instance.StartAutoUpdate();
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
