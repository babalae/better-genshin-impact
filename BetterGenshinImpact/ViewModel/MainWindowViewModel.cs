using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly IConfigService _configService;
        public string Title => $"BetterGI · 更好的原神 · {Global.Version}";

        public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
        {
            _configService = configService;
            _logger = App.GetLogger<MainWindowViewModel>();
        }


        [RelayCommand]
        private void OnLoaded()
        {
            _logger.LogInformation("更好的原神 {Version}", Global.Version);
            Task.Run(() =>
            {
                var s = OcrFactory.Paddle.Ocr(new Mat(Global.Absolute("Assets\\Model\\PaddleOCR\\test_ocr.png"), ImreadModes.Grayscale));
                Debug.WriteLine("PaddleOcr预热结果:" + s);
            });
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