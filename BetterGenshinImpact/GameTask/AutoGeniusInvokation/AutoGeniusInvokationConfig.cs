using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation;

/// <summary>
/// 自动打牌配置
/// </summary>
[Serializable]
public partial class AutoGeniusInvokationConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "1.莫娜砂糖琴";

    [ObservableProperty] private int _sleepDelay = 0;

    public List<Rect> DefaultCharacterCardRects { get;} = new List<Rect>()
    {
        new(667, 632, 165, 282),
        new(877, 632, 165, 282),
        new(1088, 632, 165, 282)
    };


    /// <summary>
    /// 骰子数量文字识别区域
    /// </summary>
    public Rect MyDiceCountRect { get; } = new(58, 632, 45, 47); // 42,47

    /// <summary>
    /// 角色卡牌区域向左扩展距离，包含HP区域
    /// </summary>
    public int CharacterCardLeftExtend { get; } = 20;

    /// <summary>
    /// 角色卡牌区域向右扩展距离，包含充能区域
    /// </summary>
    public int CharacterCardRightExtend { get; } = 14;

    /// <summary>
    /// 出战角色卡牌区域向上或者向下的距离差
    /// </summary>
    public int ActiveCharacterCardSpace { get; } = 41;

    /// <summary>
    /// HP区域 在 角色卡牌区域 的相对位置
    /// </summary>
    public Rect CharacterCardExtendHpRect { get; } = new(-20, 0, 60, 55);
}