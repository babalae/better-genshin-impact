using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<MaskButton> _maskButtons = new();

        public AllConfig Config { get; set; }

        [ObservableProperty] private Visibility _logTextBoxVisibility = Visibility.Visible;

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
                    UIDispatcherHelper.Invoke(() => { _maskButtons.Clear(); });
                }
            });
        }

        [RelayCommand]
        private void OnLoaded()
        {
            // 这个窗口比较特殊，无法直接使用构造函数依赖注入
            var configService = App.GetService<IConfigService>();
            if (configService != null)
            {
                Config = configService.Get();
                LogTextBoxVisibility = Config.MaskWindowConfig.ShowLogBox ? Visibility.Visible: Visibility.Collapsed;
            }
        }
    }
}