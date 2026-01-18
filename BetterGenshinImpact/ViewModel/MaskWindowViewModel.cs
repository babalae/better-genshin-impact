using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Overlay;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using PresentMonFps;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = [];

        public AllConfig? Config { get; set; }

        [ObservableProperty] private string _fps = "0";

        [ObservableProperty] private double _maskWindowWidth;

        [ObservableProperty] private double _maskWindowHeight;

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
        private void OnOverlayLayoutCommitted(OverlayLayoutCommittedEventArgs args)
        {
            if (Config == null)
            {
                return;
            }

            if (args.Width <= 0 || args.Height <= 0)
            {
                return;
            }

            if (MaskWindowWidth <= 0 || MaskWindowHeight <= 0)
            {
                return;
            }

            var leftRatio = ToRatio(args.Left, MaskWindowWidth);
            var topRatio = ToRatio(args.Top, MaskWindowHeight);
            var widthRatio = ToRatio(args.Width, MaskWindowWidth);
            var heightRatio = ToRatio(args.Height, MaskWindowHeight);

            switch (args.LayoutKey)
            {
                case "LogTextBox":
                    Config.MaskWindowConfig.LogTextBoxLeftRatio = leftRatio;
                    Config.MaskWindowConfig.LogTextBoxTopRatio = topRatio;
                    Config.MaskWindowConfig.LogTextBoxWidthRatio = widthRatio;
                    Config.MaskWindowConfig.LogTextBoxHeightRatio = heightRatio;
                    break;
                case "StatusList":
                    Config.MaskWindowConfig.StatusListLeftRatio = leftRatio;
                    Config.MaskWindowConfig.StatusListTopRatio = topRatio;
                    Config.MaskWindowConfig.StatusListWidthRatio = widthRatio;
                    Config.MaskWindowConfig.StatusListHeightRatio = heightRatio;
                    break;
            }
        }

        [RelayCommand]
        private void OnWindowSizeChanged(SizeChangedEventArgs args)
        {
            MaskWindowWidth = args.NewSize.Width;
            MaskWindowHeight = args.NewSize.Height;
        }

        [RelayCommand]
        private void OnExitOverlayLayoutEditMode()
        {
            if (Config == null)
            {
                return;
            }

            Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
            SystemControl.ActivateWindow();
        }

        private static double ToRatio(double value, double baseSize)
        {
            if (double.IsNaN(value) || double.IsNaN(baseSize) || baseSize <= 0)
            {
                return 0;
            }

            var ratio = value / baseSize;
            return ratio switch
            {
                < 0 => 0,
                > 1 => 1,
                _ => ratio
            };
        }
    }
}
