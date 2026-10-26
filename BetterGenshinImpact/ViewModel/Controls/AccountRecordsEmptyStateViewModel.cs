using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>维护狛荷屋空内容画面的随机提示语句。</summary>
public partial class AccountRecordsEmptyStateViewModel : ObservableObject
{
    /// <summary>开发期间使用的占位语句库，命名定案后再接入翻译。</summary>
    // 也可加入便于使用的提示语
    private static readonly string[] _messages =
    [
        "...喵？",
        "有什么快递要送吗？",
        "感谢选择「狛荷屋」！"
    ];

    /// <summary>当前展示的提示语句。</summary>
    [ObservableProperty]
    private string _message = _messages[0];

    /// <summary>每次占位控件加载时随机选择一句，允许连续抽中同一句。</summary>
    [RelayCommand]
    private void RefreshMessage()
    {
        Message = _messages[Random.Shared.Next(_messages.Length)];
    }
}
