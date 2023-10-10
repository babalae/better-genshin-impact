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
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask;
using System;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<MaskButton> _maskButtons = new();

        public AllConfig? Config { get; set; }

        [ObservableProperty] private Visibility _logTextBoxVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _uidCoverVisibility = Visibility.Visible;
        [ObservableProperty] private Rect _uidCoverRect = new(0, 0, 200, 30);

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
                else if (msg.PropertyName == "RefreshSettings")
                {
                    UIDispatcherHelper.Invoke(RefreshSettings);
                }
            });
        }

        [RelayCommand]
        private void OnLoaded()
        {
            RefreshSettings();
        }

        private void RefreshSettings()
        {
            InitConfig();
            if (Config != null)
            {
                // 日志窗口
                LogTextBoxVisibility = Config.MaskWindowConfig.ShowLogBox ? Visibility.Visible : Visibility.Collapsed;

                // UID遮盖
                UidCoverVisibility = Config.MaskWindowConfig.UidCoverEnabled ? Visibility.Visible : Visibility.Collapsed;
                // 比较特殊，必须要启动过任务调度器才能够获取到缩放信息
                if (TaskContext.Instance().SystemInfo != null)
                {
                    var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
                    var dpiScale = TaskContext.Instance().DpiScale;
                    UidCoverRect = new Rect(Config.MaskWindowConfig.UidCoverRect.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Y * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Width * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Height * assetScale / dpiScale);
                }
            }
        }

        /// <summary>
        /// 这个窗口比较特殊，无法直接使用构造函数依赖注入
        /// </summary>
        private void InitConfig()
        {
            if (Config == null)
            {
                var configService = App.GetService<IConfigService>();
                if (configService != null)
                {
                    Config = configService.Get();
                }
            }
        }
    }
}