using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class FeedWindowViewModel : ViewModel
{
    [ObservableProperty] private ObservableCollection<FeedItem> _feedItems = new();

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

    [RelayCommand]
    private void CopyItemCodes(FeedItem item)
    {
        try
        {
            if (item?.Codes != null && item.Codes.Any())
            {
                var codes = string.Join("\n", item.Codes);
                UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(codes));
                RedeemCodeManager.AddNotDetectClipboardText(codes);
                Toast.Information("兑换码已复制到剪贴板");
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"复制兑换码失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AutoRedeemItem(FeedItem item)
    {
        if (item?.Codes != null && item.Codes.Count != 0)
        {
            await new TaskRunner().RunSoloTaskAsync(new UseRedemptionCodeTask(item.Codes));
        }
    }

    private void LoadSampleData()
    {
        FeedItems.Clear();

        var sampleFeeds = new[]
        {
            new FeedItem
            {
                Title = "五周年兑换码",
                Codes = ["原神5周年快乐"],
                Time = "2025-09-28 11:30"
            },
            new FeedItem
            {
                Title = "五周年音乐APP兑换码",
                Time = "2025-09-28 10:15",
                Codes = ["SY2Y3FHHGKTE", "AY2Y3WZGY3TJ", "VYKGJWZHY2AN", "CY2H2XHYZKCS", "NG3ZJXHYG3CW", "NG2Z3EHYYKVJ"]
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
    [ObservableProperty] private string _title = string.Empty;

    [ObservableProperty] private string _content = string.Empty;

    [ObservableProperty] private string _time = string.Empty;

    [ObservableProperty] private string _tag = string.Empty;

    [ObservableProperty] private bool _hasTag = false;

    [ObservableProperty] private List<string> _codes = new();
}