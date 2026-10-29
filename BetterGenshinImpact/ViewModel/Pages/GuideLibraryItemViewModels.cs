using BetterGenshinImpact.Service.Guide;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 设置页收藏列表中的一项；标题可以直接编辑，编辑结束后重命名收藏
/// </summary>
public partial class GuideFavoriteItemViewModel : ObservableObject
{
    private readonly GuideLibraryStore _library;

    /// <summary>
    /// 标题
    /// </summary>
    [ObservableProperty]
    private string _title;

    /// <param name="library">收藏与观看记录</param>
    /// <param name="favorite">收藏</param>
    /// <param name="progress">观看进度说明</param>
    public GuideFavoriteItemViewModel(GuideLibraryStore library, GuideFavorite favorite, string progress)
    {
        _library = library;
        Id = favorite.Id;
        Url = favorite.Url;
        _title = favorite.Title;
        Progress = progress;
    }

    /// <summary>收藏 Id</summary>
    public string Id { get; }

    /// <summary>网址</summary>
    public string Url { get; }

    /// <summary>观看进度说明</summary>
    public string Progress { get; }

    /// <summary>
    /// 标题编辑后重命名收藏
    /// </summary>
    partial void OnTitleChanged(string value)
    {
        _library.RenameFavorite(Id, value);
    }
}

/// <summary>
/// 设置页观看记录列表中的一项
/// </summary>
/// <param name="record">观看记录</param>
/// <param name="progress">观看进度说明</param>
public sealed class GuideHistoryItemViewModel(GuideWatchRecord record, string progress)
{
    /// <summary>视频标识</summary>
    public string VideoKey { get; } = record.VideoKey;

    /// <summary>标题</summary>
    public string Title { get; } = string.IsNullOrWhiteSpace(record.Title) ? record.Url : record.Title;

    /// <summary>网址</summary>
    public string Url { get; } = record.Url;

    /// <summary>观看进度说明</summary>
    public string Progress { get; } = progress;
}

/// <summary>
/// 设置页下拉框的一个选项
/// </summary>
/// <param name="Value">取值</param>
/// <param name="Name">显示名称</param>
public sealed record GuideSettingOption(object Value, string Name);
