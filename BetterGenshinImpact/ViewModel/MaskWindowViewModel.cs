using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Test;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Vanara.PInvoke;
using System.Windows.Threading;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private string[] _modeNames = WindowCaptureFactory.ModeNames();

        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private int _x;
        [ObservableProperty] private int _y;
        [ObservableProperty] private int _w;
        [ObservableProperty] private int _h;

        private ILogger<MaskWindowViewModel>? _logger;

        public MaskWindowViewModel()
        {
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<IntPtr>>(this, (sender, msg) =>
            {
                User32.GetWindowRect(msg.NewValue, out var rect);
                //_windowRect.X = (int)Math.Ceiling(rect.X / DpiHelper.ScaleY);
                //_windowRect.Y = (int)Math.Ceiling(rect.Y / DpiHelper.ScaleY);
                //_windowRect.Width = (int)Math.Ceiling(rect.Width / DpiHelper.ScaleY);
                //_windowRect.Height = (int)Math.Ceiling(rect.Height / DpiHelper.ScaleY);
                _x = (int)Math.Ceiling(rect.X / DpiHelper.ScaleY);
                _y = (int)Math.Ceiling(rect.Y / DpiHelper.ScaleY);
                _w = (int)Math.Ceiling(rect.Width / DpiHelper.ScaleY);
                _h = (int)Math.Ceiling(rect.Height / DpiHelper.ScaleY);
                Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
                Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{_w} x {_h}");
            });
        }

        [RelayCommand]
        private void OnLoaded()
        {
            _logger = App.GetLogger<MaskWindowViewModel>();
            _logger.LogInformation("遮罩窗口启OnLoaded");
        }
    }
}