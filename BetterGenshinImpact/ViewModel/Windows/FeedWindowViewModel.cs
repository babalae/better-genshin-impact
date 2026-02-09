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
    [ObservableProperty] private bool _isDisplayBtnGetLiveCodes;

    private readonly HttpClient _httpClient = HttpClientFactory.GetCommonSendClient();
    private const string CodesJsonUrl = "https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/codes.json";

    public FeedWindowViewModel()
    {
        IsDisplayBtnGetLiveCodes = GamePreviewLiveDateCalculator.IsWithinPreviewRange(DateTime.Now);
    }

    [RelayCommand]
    private async Task GetLiveRedeemCodes()
    {
        IsLoading = true;
        try
        {
            var getter = new GetLiveRedeemCode();
            var codeList = await getter.GetCodeMsgAsync();

            if (codeList.Count == 0)
            {
                Toast.Warning(Lang.S["Window_1069_e54374"]);
                return;
            }

            var displayItems = codeList
                .Select(c => string.IsNullOrWhiteSpace(c.Items) ? null : c.Items)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var item = new FeedItem
            {
                Title = Lang.S["Window_1070_3dbdb6"],
                Content = displayItems.Count > 0 ? string.Join("\n", displayItems) : string.Empty,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Codes = codeList.Select(c => c.Code).ToList()
            };

            // 插入到列表顶部，方便查看
            if (FeedItems.Count > 0 && FeedItems[0].Title == item.Title)
            {
                // 如果已经存在相同标题的项，则更新内容和时间
                FeedItems[0].Content = item.Content;
                FeedItems[0].Time = item.Time;
                FeedItems[0].Codes = item.Codes;
            }
            else
            {
                FeedItems.Insert(0, item);
            }


            Toast.Success(Lang.S["Window_1071_308912"]);
        }
        catch (Exception ex)
        {
            Toast.Error($"{Lang.S["Gen_12472_0339c8"]});
        }
        finally
        {
            IsLoading = false;
        }
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
                Toast.Information(Lang.S["Window_1072_6b4ed6"]);
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"{Lang.S["Gen_12471_94dd33"]});
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
            Toast.Error($"{Lang.S["Gen_12470_82ed44"]});
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