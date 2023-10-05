using BetterGenshinImpact.Service;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Configuration;
using Wpf.Ui;

namespace BetterGenshinImpact
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
        // https://docs.microsoft.com/dotnet/core/extensions/generic-host
        // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
        // https://docs.microsoft.com/dotnet/core/extensions/configuration
        // https://docs.microsoft.com/dotnet/core/extensions/logging
        private static readonly IHost _host = Host.CreateDefaultBuilder()
            .ConfigureServices(
                (context, services) =>
                {
                    var logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"log");
                    Directory.CreateDirectory(logFolder);
                    var logFile = Path.Combine(logFolder, $"better-genshin-impact.log");

                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.File(path: logFile, outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}", rollingInterval: RollingInterval.Day)
                        .WriteTo.RichTextBox(MaskWindow.Instance().LogBox, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                        .CreateLogger();
                    services.AddLogging(c => c.AddSerilog());


                    // App Host
                    services.AddHostedService<ApplicationHostService>();

                    // Page resolver service
                    services.AddSingleton<IPageService, PageService>();

                    // Service containing navigation, same as INavigationWindow... but without window
                    services.AddSingleton<INavigationService, NavigationService>();

                    // Main window with navigation
                    services.AddSingleton<INavigationWindow, MainWindow>();
                    services.AddSingleton<MainWindowViewModel>();

                    // Views and ViewModels
                    services.AddSingleton<HomePage>();
                    services.AddSingleton<HomePageViewModel>();
                    services.AddSingleton<TriggerSettingsPage>();
                    services.AddSingleton<TriggerSettingsPageViewModel>();
                    services.AddSingleton<MacroSettingsPage>();

                    // Configuration
                    //services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));
                }
            )
            .Build();

        public static ILogger<T> GetLogger<T>()
        {
            return _host.Services.GetService<ILogger<T>>()!;
        }

        /// <summary>
        /// Gets registered service.
        /// </summary>
        /// <typeparam name="T">Type of the service to get.</typeparam>
        /// <returns>Instance of the service or <see langword="null"/>.</returns>
        public static T? GetService<T>() where T : class
        {
            return _host.Services.GetService(typeof(T)) as T;
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            await _host.StartAsync();
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }
    }
}