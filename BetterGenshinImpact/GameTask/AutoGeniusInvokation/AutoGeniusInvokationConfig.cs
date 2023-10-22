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

    public List<Rect> DefaultCharacterCardRects { get; set; } = new List<Rect>()
    {
        new(667, 632, 165, 282),
        new(877, 632, 165, 282),
        new(1088, 632, 165, 282)
    };
}