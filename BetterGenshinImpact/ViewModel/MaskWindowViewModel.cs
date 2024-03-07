using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
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

        public AllConfig? Config { get; set; }

        [ObservableProperty] private Rect _uidCoverRect = new(0, 0, 200, 30);

        [ObservableProperty] private Point _eastPoint = new(274, 109);
        [ObservableProperty] private Point _southPoint = new(150, 233);
        [ObservableProperty] private Point _westPoint = new(32, 109);
        [ObservableProperty] private Point _northPoint = new(150, -9);


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
                OnPropertyChanged(nameof(Config));
                // 比较特殊，必须要启动过任务调度器才能够获取到缩放信息
                if (TaskContext.Instance().SystemInfo != null)
                {
                    var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
                    var dpiScale = TaskContext.Instance().DpiScale;
                    UidCoverRect = new Rect(Config.MaskWindowConfig.UidCoverRect.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Y * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Width * assetScale / dpiScale,
                        Config.MaskWindowConfig.UidCoverRect.Height * assetScale / dpiScale);
                    EastPoint = new Point(Config.MaskWindowConfig.EastPoint.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.EastPoint.Y * assetScale / dpiScale);
                    SouthPoint = new Point(Config.MaskWindowConfig.SouthPoint.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.SouthPoint.Y * assetScale / dpiScale);
                    WestPoint = new Point(Config.MaskWindowConfig.WestPoint.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.WestPoint.Y * assetScale / dpiScale);
                    NorthPoint = new Point(Config.MaskWindowConfig.NorthPoint.X * assetScale / dpiScale,
                        Config.MaskWindowConfig.NorthPoint.Y * assetScale / dpiScale);
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