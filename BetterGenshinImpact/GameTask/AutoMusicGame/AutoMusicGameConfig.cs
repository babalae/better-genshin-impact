using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;


/// <summary>
/// 千音雅集配置
/// </summary>
[Serializable]
public partial class AutoMusicGameConfig : ObservableObject
{
    [ObservableProperty] private string _modeName = "";
    
    public static readonly List<string> MusicModelList = ["仅获取乐曲奖励", "所有乐曲达成【大音天籁】"];
}