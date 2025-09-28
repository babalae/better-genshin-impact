using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class FeedWindowViewModel : ViewModel
{
    [ObservableProperty]
    private ObservableCollection<FeedItem> _feedItems = new();

    public FeedWindowViewModel()
    {
        LoadSampleData();
    }

    [RelayCommand]
    private void Refresh()
    {
        // 刷新数据逻辑
        LoadSampleData();
    }

    private void LoadSampleData()
    {
        FeedItems.Clear();
        
        var sampleFeeds = new[]
        {
            new FeedItem
            {
                Title = "五周年兑换码",
                Content = "原神5周年快乐",
                Time = "2025-09-28 11:30",
                Tag = "活动",
                HasTag = true
            },
            new FeedItem
            {
                Title = "五周年音乐APP兑换码",
                Content = "SY2Y3FHHGKTE\nAY2Y3WZGY3TJ\nVYKGJWZHY2AN\nCY2H2XHYZKCS\nNG3ZJXHYG3CW\nNG2Z3EHYYKVJ",
                Time = "2025-09-28 10:15",
                Tag = "第三方",
                HasTag = true
            }
        };

        foreach (var feed in sampleFeeds)
        {
            FeedItems.Add(feed);
        }
    }
}

public partial class FeedItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _time = string.Empty;

    [ObservableProperty]
    private string _tag = string.Empty;

    [ObservableProperty]
    private bool _hasTag = false;
}