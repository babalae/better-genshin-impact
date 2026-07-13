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
    // 自动达到大音天籁的级别
    [ObservableProperty] 
    private bool _mustCanorusLevel = false;
    
    // 乐曲级别
    [ObservableProperty] 
    private string _musicLevel = "";
}