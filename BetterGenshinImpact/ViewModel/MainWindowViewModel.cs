using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Windows;
using BetterGenshinImpact.Core;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Vanara.PInvoke;
using System.Collections.ObjectModel;
using BetterGenshinImpact.View.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly IConfigService _configService;

        public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
        {
            _configService = configService;
            _logger = App.GetLogger<MainWindowViewModel>();
        }


        [RelayCommand]
        private void OnLoaded()
        {
            _logger.LogInformation("更好的原神({Text}) Alpha {Ver}","内测版", 2.2);
        }

        [RelayCommand]
        private void OnClosed()
        {
            _configService.Save();
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "Close", "", ""));
            Debug.WriteLine("MainWindowViewModel Closed");
            Application.Current.Shutdown();
        }

    }
}