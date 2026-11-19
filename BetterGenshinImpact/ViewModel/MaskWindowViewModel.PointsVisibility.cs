using System.ComponentModel;
using BetterGenshinImpact.GameTask.MapMask;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel
{
    /// <summary>
    /// 遮罩窗口：临时隐藏全部标点（只影响显示，不修改任何标记），方便查看地图后再恢复
    /// </summary>
    public partial class MaskWindowViewModel
    {
        /// <summary>
        /// 是否临时隐藏全部标点；不保存，重启后恢复显示
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowBigMapPoints), nameof(ShowMiniMapPoints))]
        private bool _areMapPointsTemporarilyHidden;

        /// <summary>
        /// 大地图标点层是否显示
        /// </summary>
        public bool ShowBigMapPoints => IsInBigMapUi && !AreMapPointsTemporarilyHidden;

        /// <summary>
        /// 小地图标点层是否显示
        /// </summary>
        public bool ShowMiniMapPoints => Config.MapMaskConfig.MiniMapMaskEnabled && !AreMapPointsTemporarilyHidden;

        /// <summary>
        /// 订阅影响标点层显示的配置变化
        /// </summary>
        private void InitMapPointsVisibility()
        {
            Config.MapMaskConfig.PropertyChanged += OnMapMaskConfigPropertyChangedForVisibility;
        }

        /// <summary>
        /// 小地图遮罩开关变化时刷新小地图标点层
        /// </summary>
        private void OnMapMaskConfigPropertyChangedForVisibility(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapMaskConfig.MiniMapMaskEnabled))
            {
                OnPropertyChanged(nameof(ShowMiniMapPoints));
            }
        }

        /// <summary>
        /// 切换临时隐藏全部标点；隐藏时关闭点位详情弹窗
        /// </summary>
        [RelayCommand]
        private void ToggleMapPointsTemporarilyHidden()
        {
            AreMapPointsTemporarilyHidden = !AreMapPointsTemporarilyHidden;
            if (AreMapPointsTemporarilyHidden)
            {
                PointInfoPopup.Close();
            }
        }

        /// <summary>
        /// 切换“隐藏已标记的标点”（保存到配置，重启后保留）
        /// </summary>
        [RelayCommand]
        private void ToggleHideMarkedPoints()
        {
            Config.MapMaskConfig.HideMarkedPoints = !Config.MapMaskConfig.HideMarkedPoints;
        }
    }
}
