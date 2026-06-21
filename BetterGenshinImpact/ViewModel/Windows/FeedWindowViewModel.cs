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
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using Newtonsoft.Json;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class FeedWindowViewModel : ViewModel
{
    public const string CodesJsonUrl = "https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/codes.json";
    private const string GlobalServerTitlePrefix = "[国际服]";

    [ObservableProperty] private ObservableCollection<FeedItem> _cnFeedItems = new();
    [ObservableProperty] private ObservableCollection<FeedItem> _globalFeedItems = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDisplayBtnGetLiveCodes;
    [ObservableProperty] private bool _isCnServerSelected = true;
    [ObservableProperty] private bool _isGlobalServerSelected;

    private readonly HttpClient _httpClient = HttpClientFactory.GetCommonSendClient();
    public CommonConfig CommonConfig { get; }

    public FeedWindowViewModel()
    {
        var configService = App.GetService<IConfigService>();
        CommonConfig = configService?.Get().CommonConfig ?? new CommonConfig();
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
                Toast.Warning("暂无前瞻兑换码信息");
                return;
            }

            var displayItems = codeList
                .Select(c => string.IsNullOrWhiteSpace(c.Items) ? null : c.Items)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var item = new FeedItem
            {
                Title = "【实时获取】前瞻直播兑换码",
                Content = displayItems.Count > 0 ? string.Join("\n", displayItems) : string.Empty,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Codes = codeList.Select(c => c.Code).ToList()
            };

            // 插入到国服列表顶部，方便查看
            if (CnFeedItems.Count > 0 && CnFeedItems[0].Title == item.Title)
            {
                // 如果已经存在相同标题的项，则更新内容和时间
                CnFeedItems[0].Content = item.Content;
                CnFeedItems[0].Time = item.Time;
                CnFeedItems[0].Codes = item.Codes;
            }
            else
            {
                CnFeedItems.Insert(0, item);
            }


            Toast.Success("已实时获取前瞻兑换码");
        }
        catch (Exception ex)
        {
            Toast.Error($"获取前瞻兑换码失败: {ex.Message}");
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
    private void SelectCnServer()
    {
        IsCnServerSelected = true;
        IsGlobalServerSelected = false;
    }

    [RelayCommand]
    private void SelectGlobalServer()
    {
        IsCnServerSelected = false;
        IsGlobalServerSelected = true;
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
            var (cnFeeds, globalFeeds) = SplitByServer(items);

            CnFeedItems.Clear();
            foreach (var feed in cnFeeds)
            {
                CnFeedItems.Add(feed);
            }

            GlobalFeedItems.Clear();
            foreach (var feed in globalFeeds)
            {
                GlobalFeedItems.Add(feed);
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

    public static (List<FeedItem> CnFeeds, List<FeedItem> GlobalFeeds) SplitByServer(IEnumerable<FeedItem?> items)
    {
        var cnFeeds = new List<FeedItem>();
        var globalFeeds = new List<FeedItem>();

        foreach (var feed in items)
        {
            if (feed is null)
            {
                continue;
            }

            feed.HasTag = !string.IsNullOrWhiteSpace(feed.Tag);

            if (GetServerType(feed) == RedeemCodeServerType.Global)
            {
                globalFeeds.Add(feed);
            }
            else
            {
                cnFeeds.Add(feed);
            }
        }

        return (cnFeeds, globalFeeds);
    }

    public static RedeemCodeServerType GetServerType(FeedItem feed)
    {
        return feed.Title?.StartsWith(GlobalServerTitlePrefix, StringComparison.OrdinalIgnoreCase) == true
            ? RedeemCodeServerType.Global
            : RedeemCodeServerType.Cn;
    }

    public static string GetFeedVersion(IEnumerable<FeedItem> items)
    {
        return items
            .Select(item => item.Time?.Trim())
            .OfType<string>()
            .Where(time => !string.IsNullOrEmpty(time))
            .OrderByDescending(time => time, StringComparer.Ordinal)
            .FirstOrDefault() ?? string.Empty;
    }
}

public enum RedeemCodeServerType
{
    Cn,
    Global
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
