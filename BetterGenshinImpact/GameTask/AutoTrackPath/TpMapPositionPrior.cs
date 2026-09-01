using BetterGenshinImpact.GameTask.AutoPathing;
namespace BetterGenshinImpact.GameTask.AutoTrackPath;
/// <summary>
/// 快速拖动传送（TpTaskFastDrag）的小地图先验位置读取器。
/// 转发自共享 <see cref="Navigation.GetTpPriorPosition"/>（传送先验缓存，由
/// NavigationInstance.SetPrevPosition 在寻路/识别/传送落点等处同步维护），与茶包版
/// 行为逐字节一致。
/// </summary>
public static class TpMapPositionPrior
{
    /// <summary>
    /// 读传送先验专用缓存坐标（不受小地图 WarmUp/Reset 影响）。
    /// 转发自 <see cref="Navigation.GetTpPriorPosition"/>。
    /// </summary>
    public static (float X, float Y) GetTpPriorPosition() => Navigation.GetTpPriorPosition();
}