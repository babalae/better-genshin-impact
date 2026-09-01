using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 茶包快速拖动传送（TpTaskFastDrag）专属配置。
/// 自持快速传送所需的 3 个配置值（MapZoomDistanceForce / MinZoomLevel / ZoomButtonX），
/// 不再转发共享 TpConfig 的茶包扩展成员，从而自包含（PR 公版时快速传送整套带走，不依赖
/// 共享 TpConfig 里公版缺失的成员）。
///
/// 解耦纪律：只"搬移"不"改逻辑"。默认值与茶包版 TpConfig 完全一致，行为逐字节不变。
/// 注：历史上这 3 个值存放在共享 TpConfig，本次已迁到本快速传送专属配置；旧的
/// config.json 里 TpConfig 下的同名键不再读取（STJ 跳过未映射键），如需保留旧值需另行做迁移。
/// </summary>
public partial class TpTaskFastDragConfig : ObservableValidator
{
    /// <summary>
    /// 拖动额外延时系数控制值：>0 时 TpTaskFastDrag 的 _extraDelayFactor =
    /// 1.0 + MapZoomDistanceForce * 0.2；==0 时动态跑道模式（无额外延时）。
    /// </summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 5, ErrorMessage = "移动参数：0~5")]
    private double _mapZoomDistanceForce = 0;

    /// <summary>
    /// 最小缩放等级：目标传送点附近用于"拉开相邻传送点"的最放大下限（默认 2.0）。
    /// </summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1.0, 6.0)]
    private double _minZoomLevel = 2.0;

    /// <summary>
    /// 缩放比例按钮的 x 坐标（1080p 配置坐标，默认 47）。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _zoomButtonX = 47;
}
