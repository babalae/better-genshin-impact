using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Managed host of the application.
/// </summary>
public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
{
    private INavigationWindow? _navigationWindow;
    private readonly ILogger<ApplicationHostService> _logger = serviceProvider.GetRequiredService<ILogger<ApplicationHostService>>();

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Initialize localization service with proper error handling
        await InitializeLocalizationServiceAsync(cancellationToken);

        await HandleActivationAsync();
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Dispose localization service properly
        await DisposeLocalizationServiceAsync();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        await Task.CompletedTask;

        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = (serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
            _navigationWindow!.ShowWindow();
            //
            var args = Environment.GetCommandLineArgs();

            if (args.Length > 1)
            {
                
                //无论如何，先跳到主页，否则在通过参数的任务在执行完之前，不会加载快捷键
                _ = _navigationWindow.Navigate(typeof(HomePage));
                
                if (args[1].Contains("startOneDragon"))
                {

                    // 通过命令行参数启动「一条龙」 => 跳转到一条龙配置页。
                    _ = _navigationWindow.Navigate(typeof(OneDragonFlowPage));
                    // 后续代码在 OneDragonFlowViewModel / OnLoaded 中。
                }
                else if (args[1].Trim().Equals("--startGroups", StringComparison.InvariantCultureIgnoreCase))
                {
                    // 通过命令行参数启动「调度组」 => 跳转到调度器配置页。
                    _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                    if (args.Length > 2)
                    {
                        // 获取调度组
                        var names = args.Skip(2).ToArray().Select(x => x.Trim()).ToArray();
                        // 启动调度器
                        var scheduler = App.GetService<ScriptControlViewModel>();
                        scheduler?.OnStartMultiScriptGroupWithNamesAsync(names);
                    }
                }else if (args[1].Trim().Equals("--TaskProgress", StringComparison.InvariantCultureIgnoreCase))
                {

                    // 通过命令行参数启动「调度组」 => 跳转到调度器配置页。
                    _ = _navigationWindow.Navigate(typeof(ScriptControlPage));
                    if (args.Length > 1)
                    {
                        // 获取调度组
                        var names = args.Skip(2).ToArray().Select(x => x.Trim()).ToArray();
                        // 启动调度器
                        var scheduler = App.GetService<ScriptControlViewModel>();
                        scheduler?.OnStartMultiScriptTaskProgressAsync(names);
                    }
                }
                else if (args[1].Contains("start"))
                {
                    // 通过命令行参数打开「启动页开关」 => 跳转到主页。
                    _ = _navigationWindow.Navigate(typeof(HomePage));
                    // 后续代码在 HomePageViewModel / OnLoaded 中。
                }
                else
                {
                    // 其它命令行参数 => 跳转到主页。
                    _ = _navigationWindow.Navigate(typeof(HomePage));
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

    /// <summary>
    /// Initializes the localization service with proper error handling and logging
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task InitializeLocalizationServiceAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing localization service...");

            var localizationService = serviceProvider.GetService<ILocalizationService>();
            if (localizationService == null)
            {
                _logger.LogError("Localization service not found in dependency injection container");
                return;
            }

            // Initialize the localization service
            await localizationService.InitializeAsync();

            _logger.LogInformation("Localization service initialized successfully. Current language: {Language}", 
                localizationService.CurrentLanguage);

            // Log available languages for debugging
            var availableLanguages = localizationService.AvailableLanguages.ToList();
            _logger.LogDebug("Available languages: {Languages}", 
                string.Join(", ", availableLanguages.Select(l => $"{l.Code} ({l.DisplayName})")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Localization service initialization was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize localization service. Application will continue with fallback behavior.");
            
            // Don't throw here - let the application continue even if localization fails
            // The localization service should handle its own fallback mechanisms
        }
    }

    /// <summary>
    /// Properly disposes the localization service during application shutdown
    /// </summary>
    private async Task DisposeLocalizationServiceAsync()
    {
        try
        {
            _logger.LogInformation("Disposing localization service...");

            var localizationService = serviceProvider.GetService<ILocalizationService>();
            if (localizationService is IDisposable disposableService)
            {
                disposableService.Dispose();
                _logger.LogInformation("Localization service disposed successfully");
            }
            else if (localizationService != null)
            {
                _logger.LogDebug("Localization service does not implement IDisposable");
            }
            else
            {
                _logger.LogWarning("Localization service not found during disposal");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing localization service");
            // Don't throw during shutdown - log the error and continue
        }

        await Task.CompletedTask;
    }
}