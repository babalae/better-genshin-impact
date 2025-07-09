using BetterGenshinImpact.GameTask.Model.GameUI;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

[Serializable]
public partial class GetGridIconsConfig : ObservableObject
{
    /// <summary>
    /// 昼夜策略
    /// 钓全天的鱼、还是只钓白天或夜晚的鱼
    /// </summary>
    [ObservableProperty]
    private GridScreenName _gridName = GridScreenName.Weapons;

    // 使用星星作为后缀
    [ObservableProperty]
    private bool _starAsSuffix = false;

    // 使用等级作为后缀
    [ObservableProperty]
    private bool _lvAsSuffix = false;

    // 最多获取多少个图标
    [ObservableProperty]
    private int _maxNumToGet = int.MaxValue;
}