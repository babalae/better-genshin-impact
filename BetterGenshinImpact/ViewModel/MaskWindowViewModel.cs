using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using PresentMonFps;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Model.MaskMap;
using Vanara.PInvoke;
using MaskMapPoint = BetterGenshinImpact.Model.MaskMap.MaskMapPoint;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = [];

        public AllConfig? Config { get; set; }

        [ObservableProperty] private string _fps = "0";
        
        public ObservableCollection<MaskMapPoint> Points { get; } = new ObservableCollection<MaskMapPoint>();


        public MaskWindowViewModel()
        {
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
            {
                if (msg.PropertyName == "RefreshSettings")
                {
                    UIDispatcherHelper.Invoke(RefreshSettings);
                }
            });
        }

        private void InitializeStatusList()
        {
            if (Config != null)
            {
                StatusList.Add(new StatusItem("\uf256 拾取", Config.AutoPickConfig));
                StatusList.Add(new StatusItem("\uf075 剧情", Config.AutoSkipConfig));
                StatusList.Add(new StatusItem("\ue5c8 邀约", Config.AutoSkipConfig, "AutoHangoutEventEnabled"));
                StatusList.Add(new StatusItem("\uf578 钓鱼", Config.AutoFishingConfig));
                StatusList.Add(new StatusItem("\uf3c5 传送", Config.QuickTeleportConfig));
            }
        }

        [RelayCommand]
        private void OnLoaded()
        {
            RefreshSettings();
            InitializeStatusList();
            InitFps();
        }

        private void RefreshSettings()
        {
            InitConfig();
            if (Config != null)
            {
                OnPropertyChanged(nameof(Config));
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

        private void InitFps()
        {
            if (Config!.MaskWindowConfig.ShowFps)
            {
                nint targetHWnd = TaskContext.Instance().GameHandle;
                _ = User32.GetWindowThreadProcessId(targetHWnd, out var pid);
                Task.Run(async () =>
                {
                    await FpsInspector.StartForeverAsync(new FpsRequest(pid), (result) =>
                    {
                        Fps = $"{result.Fps:0}";
                    });
                });
            }
        }
        
        [RelayCommand]
        private void OnPointClick(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 在这里触发 UI 或逻辑（示例：弹窗）
                MessageBox.Show($"点击了点: {point.Id}");
            }
        }

        [RelayCommand]
        private void OnPointRightClick(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 自定义右键逻辑
            }
        }

        [RelayCommand]
        private void OnPointHover(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 悬停逻辑
            }
        }
    }
}
