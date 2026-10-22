using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 截取物品图标任务的参数。
/// </summary>
public class GetGridIconsParam
{
    /// <summary>
    /// 要截取图标的 Grid 界面。
    /// </summary>
    public GridScreenName GridName { get; set; } = GridScreenName.Weapons;

    /// <summary>
    /// 是否把星级作为文件名后缀。
    /// </summary>
    public bool StarAsSuffix { get; set; }

    /// <summary>
    /// 最多截取的图标数量，为空表示不限。
    /// </summary>
    public int? MaxNumToGet { get; set; }
}
