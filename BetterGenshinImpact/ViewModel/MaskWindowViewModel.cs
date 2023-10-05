using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Test;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Vanara.PInvoke;
using System.Windows.Threading;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        private ILogger<MaskWindowViewModel>? _logger;

        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<MaskButton> _maskButtons = new();


        public MaskWindowViewModel()
        {
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
            {
                if (msg.PropertyName == "AddButton")
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        if (msg.NewValue is MaskButton button && !_maskButtons.Contains(button))
                        {
                            _maskButtons.Add(button);
                        }
                    });
                }
                else if (msg.PropertyName == "RemoveButton")
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        if (msg.NewValue is string buttonName)
                        {
                            var button = _maskButtons.FirstOrDefault(b => b.Name == buttonName);
                            if (button != null)
                            {
                                _maskButtons.Remove(button);
                            }
                        }
                    });
                }
                else if (msg.PropertyName == "RemoveAllButton")
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        _maskButtons.Clear();
                    });
                }
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