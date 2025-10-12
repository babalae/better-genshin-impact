using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using Newtonsoft.Json;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class FeedWindowViewModel : ViewModel
{
    [ObservableProperty] private ObservableCollection<FeedItem> _feedItems = new();
    [ObservableProperty] private bool _isLoading;

    private readonly HttpClient _httpClient = HttpClientFactory.GetCommonSendClient();
    private const string CodesJsonUrl = "https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/codes.json";

    public FeedWindowViewModel()
    {
        // 初始数据在窗口加载时触发远程拉取
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadRemoteDataAsync();
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

    public async Task LoadRemoteDataAsync()
    {
        IsLoading = true;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, CodesJsonUrl);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            var items = JsonConvert.DeserializeObject<List<FeedItem>>(json) ?? [];

            FeedItems.Clear();
            foreach (var feed in items)
            {
                // 若存在标签文本，设置 HasTag
                feed.HasTag = !string.IsNullOrWhiteSpace(feed.Tag);
                FeedItems.Add(feed);
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"获取兑换码失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
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
    
    [ObservableProperty] private string _valid = string.Empty;
}