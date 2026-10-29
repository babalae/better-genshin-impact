using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “悬浮小窗”设置页：外观、鼠标与焦点、自动显示隐藏、播放习惯、字幕浮窗、快捷键、收藏与观看记录（见设计文档 12.1 节）
/// </summary>
public partial class GuideWindowSettingsPageViewModel : ViewModel
{
    private readonly IGuideWindowService _service;
    private readonly GuideLibraryStore _library;
    private readonly HotKeyPageViewModel _hotKeyPage;
    private readonly INavigationService _navigationService;
    private readonly Func<StorageManagementWindow> _storageWindowFactory;

    /// <summary>
    /// 收藏或观看记录变化后合并刷新列表
    /// </summary>
    private readonly DispatcherTimer _libraryRefreshTimer;

    /// <summary>
    /// 状态说明，例如“正在显示”“已隐藏：游戏在后台”
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// 快捷键摘要，例如“已绑定 12 / 21”
    /// </summary>
    [ObservableProperty]
    private string _hotKeySummary = string.Empty;

    /// <summary>
    /// 收藏与观看记录摘要
    /// </summary>
    [ObservableProperty]
    private string _librarySummary = string.Empty;

    /// <param name="service">小窗服务</param>
    /// <param name="library">收藏与观看记录</param>
    /// <param name="hotKeyPage">快捷键页（单例），共用“悬浮小窗”分组的快捷键对象</param>
    /// <param name="navigationService">主窗口导航</param>
    /// <param name="storageWindowFactory">存储空间窗口</param>
    public GuideWindowSettingsPageViewModel(IGuideWindowService service, GuideLibraryStore library,
        HotKeyPageViewModel hotKeyPage, INavigationService navigationService, Func<StorageManagementWindow> storageWindowFactory)
    {
        _service = service;
        _library = library;
        _hotKeyPage = hotKeyPage;
        _navigationService = navigationService;
        _storageWindowFactory = storageWindowFactory;
        _libraryRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => RefreshLibrary(), Application.Current.Dispatcher);
        _libraryRefreshTimer.Stop();

        State.PropertyChanged += OnStateChanged;
        _library.Changed += OnLibraryChanged;
        foreach (var hotKey in HotKeys)
        {
            hotKey.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HotKeySettingModel.HotKey))
                {
                    UpdateHotKeySummary();
                }
            };
        }

        UpdateStatus();
        UpdateHotKeySummary();
        RefreshLibrary();
    }

    /// <summary>
    /// 小窗状态
    /// </summary>
    public GuideWindowState State => _service.State;

    /// <summary>
    /// 小窗配置
    /// </summary>
    public GuideWindowConfig Config => _service.Config;

    /// <summary>
    /// 当前实例能否使用小窗
    /// </summary>
    public bool IsAvailable => _service.IsAvailable;

    /// <summary>
    /// “悬浮小窗”分组的快捷键，与快捷键页是同一组对象
    /// </summary>
    public ObservableCollection<HotKeySettingModel> HotKeys => _hotKeyPage.GuideHotKeyDirectory.Children;

    /// <summary>
    /// 收藏列表
    /// </summary>
    public ObservableCollection<GuideFavoriteItemViewModel> Favorites { get; } = [];

    /// <summary>
    /// 最近观看列表
    /// </summary>
    public ObservableCollection<GuideHistoryItemViewModel> History { get; } = [];

    /// <summary>
    /// 是否有收藏
    /// </summary>
    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>
    /// 是否有观看记录
    /// </summary>
    public bool HasHistory => History.Count > 0;

    #region 下拉框选项

    /// <summary>进入游戏时自动打开</summary>
    public GuideSettingOption[] AutoOpenOptions { get; } =
    [
        new(GuideAutoOpenMode.RestoreLast, "恢复上次状态"),
        new(GuideAutoOpenMode.Always, "总是打开"),
        new(GuideAutoOpenMode.Never, "不自动打开"),
    ];

    /// <summary>位置模式</summary>
    public GuideSettingOption[] PositionModeOptions { get; } =
    [
        new(GuidePositionMode.FollowGame, "跟随游戏窗口"),
        new(GuidePositionMode.Free, "自由放置"),
    ];

    /// <summary>续播</summary>
    public GuideSettingOption[] ResumeModeOptions { get; } =
    [
        new(GuideResumeMode.FromLibrary, "从收藏或观看记录打开时"),
        new(GuideResumeMode.Always, "打开任何看过的视频时"),
        new(GuideResumeMode.Never, "不续播"),
    ];

    /// <summary>交互模式自动结束</summary>
    public GuideSettingOption[] InteractionExitOptions { get; } =
    [
        new(1, "鼠标离开 1 秒后"),
        new(2, "鼠标离开 2 秒后"),
        new(5, "鼠标离开 5 秒后"),
        new(0, "不自动结束"),
    ];

    /// <summary>快进、快退步长</summary>
    public GuideSettingOption[] SeekOptions { get; } =
    [
        new(3, "3 秒"), new(5, "5 秒"), new(10, "10 秒"), new(15, "15 秒"), new(30, "30 秒"),
    ];

    /// <summary>倍速步长</summary>
    public GuideSettingOption[] RateStepOptions { get; } =
    [
        new(0.1, "0.1x"), new(0.25, "0.25x"), new(0.5, "0.5x"),
    ];

    /// <summary>临时加速倍速</summary>
    public GuideSettingOption[] BoostRateOptions { get; } =
    [
        new(1.5, "1.5x"), new(2.0, "2x"), new(2.5, "2.5x"), new(3.0, "3x"), new(4.0, "4x"),
    ];

    /// <summary>音量步长</summary>
    public GuideSettingOption[] VolumeStepOptions { get; } =
    [
        new(0.05, "5%"), new(0.1, "10%"), new(0.2, "20%"),
    ];

    /// <summary>网页缩放</summary>
    public GuideSettingOption[] ZoomOptions { get; } =
    [
        new(0.67, "67%"), new(0.8, "80%"), new(0.9, "90%"), new(1.0, "100%"), new(1.1, "110%"), new(1.25, "125%"),
    ];

    /// <summary>
    /// 位置模式：经服务切换，保留小窗当前位置
    /// </summary>
    public GuidePositionMode PositionMode
    {
        get => Config.PositionMode;
        set
        {
            if (Config.PositionMode == value)
            {
                return;
            }

            _service.SetPositionMode(value);
            OnPropertyChanged();
        }
    }

    #endregion

    /// <inheritdoc />
    public override void OnNavigatedTo()
    {
        UpdateStatus();
        UpdateHotKeySummary();
        RefreshLibrary();
    }

    #region 小窗操作

    /// <summary>打开小窗</summary>
    [RelayCommand]
    private void OpenWindow() => _service.Open();

    /// <summary>隐藏小窗</summary>
    [RelayCommand]
    private void HideWindow() => _service.Hide();

    /// <summary>关闭小窗</summary>
    [RelayCommand]
    private void CloseWindow() => _service.Close();

    /// <summary>紧急恢复小窗</summary>
    [RelayCommand]
    private void RestoreWindow() => _service.RestoreEmergency();

    /// <summary>主页恢复为 B 站首页</summary>
    [RelayCommand]
    private void ResetHomeUrl() => Config.HomeUrl = GuideWindowConfig.DefaultHomeUrl;

    /// <summary>调整字幕浮窗位置</summary>
    [RelayCommand]
    private void AdjustSubtitle()
    {
        Config.SubtitleWindowEnabled = true;
        if (!State.IsOpen)
        {
            _service.Open();
        }

        _service.BeginSubtitleAdjust();
    }

    /// <summary>字幕浮窗恢复默认位置与宽度</summary>
    [RelayCommand]
    private void ResetSubtitlePosition()
    {
        Config.SubtitleCenterXRatio = 0.5;
        Config.SubtitleBottomRatio = 0.22;
        Config.SubtitleWidthRatio = 0.6;
        Toast.Information(I18nService.Instance.Translate("字幕浮窗已恢复默认位置"));
    }

    /// <summary>打开存储空间窗口</summary>
    [RelayCommand]
    private void OpenStorage()
    {
        var window = _storageWindowFactory();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    /// <summary>打开快捷键页</summary>
    [RelayCommand]
    private void OpenHotKeyPage() => _navigationService.Navigate(typeof(HotKeyPage));

    /// <summary>
    /// 一键填入推荐快捷键
    /// </summary>
    [RelayCommand]
    private void ApplyRecommendedHotKeys()
    {
        var (filled, skipped) = _hotKeyPage.ApplyGuideRecommendedHotKeys();
        UpdateHotKeySummary();
        if (filled == 0 && skipped == 0)
        {
            Toast.Information(I18nService.Instance.Translate("悬浮小窗的快捷键都已绑定"));
            return;
        }

        var message = skipped > 0
            ? string.Format(I18nService.Instance.Translate("已填入 {0} 项推荐快捷键，{1} 项因与其他功能或程序冲突已跳过"), filled, skipped)
            : string.Format(I18nService.Instance.Translate("已填入 {0} 项推荐快捷键"), filled);
        if (skipped > 0)
        {
            Toast.Warning(message);
        }
        else
        {
            Toast.Success(message);
        }
    }

    #endregion

    #region 收藏与观看记录

    /// <summary>打开收藏</summary>
    [RelayCommand]
    private void OpenFavorite(GuideFavoriteItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _library.MarkFavoriteOpened(item.Id);
        _service.Open(new GuideOpenRequest(item.Url, GuideOpenSource.Favorite));
    }

    /// <summary>删除收藏</summary>
    [RelayCommand]
    private void RemoveFavorite(GuideFavoriteItemViewModel? item)
    {
        if (item is not null)
        {
            _library.RemoveFavorite(item.Id);
        }
    }

    /// <summary>打开观看记录</summary>
    [RelayCommand]
    private void OpenHistory(GuideHistoryItemViewModel? item)
    {
        if (item is not null)
        {
            _service.Open(new GuideOpenRequest(item.Url, GuideOpenSource.History));
        }
    }

    /// <summary>删除观看记录</summary>
    [RelayCommand]
    private void RemoveHistory(GuideHistoryItemViewModel? item)
    {
        if (item is not null)
        {
            _library.RemoveRecord(item.VideoKey);
        }
    }

    /// <summary>清空观看记录</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        var result = ThemedMessageBox.Question(
            I18nService.Instance.Translate("清空全部观看记录？收藏不受影响，清空后无法从上次位置继续观看。"),
            I18nService.Instance.Translate("清空观看记录"),
            MessageBoxButton.OKCancel,
            MessageBoxResult.Cancel);
        if (result == MessageBoxResult.OK)
        {
            _library.ClearHistory();
        }
    }

    /// <summary>
    /// 收藏或观看记录变化（可能在任意线程）：合并后在界面线程刷新
    /// </summary>
    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _libraryRefreshTimer.Stop();
            _libraryRefreshTimer.Start();
        });
    }

    /// <summary>
    /// 重新读取收藏与观看记录
    /// </summary>
    private void RefreshLibrary()
    {
        _libraryRefreshTimer.Stop();
        Favorites.Clear();
        foreach (var favorite in _library.GetFavorites())
        {
            Favorites.Add(new GuideFavoriteItemViewModel(_library, favorite, DescribeProgress(_library.FindRecord(favorite.VideoKey))));
        }

        History.Clear();
        foreach (var record in _library.GetHistory().Take(50))
        {
            History.Add(new GuideHistoryItemViewModel(record, DescribeProgress(record)));
        }

        LibrarySummary = string.Format(I18nService.Instance.Translate("收藏 {0} · 观看记录 {1}"), _library.FavoriteCount, _library.HistoryCount);
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>
    /// 进度说明
    /// </summary>
    private static string DescribeProgress(GuideWatchRecord? record)
    {
        if (record is null)
        {
            return string.Empty;
        }

        if (record.Finished)
        {
            return "已看完";
        }

        return record.Duration > 0
            ? $"看到 {GuideWindowState.FormatTime(record.Position)} / {GuideWindowState.FormatTime(record.Duration)}"
            : $"看到 {GuideWindowState.FormatTime(record.Position)}";
    }

    #endregion

    #region 摘要

    /// <summary>
    /// 小窗状态变化时更新状态说明
    /// </summary>
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GuideWindowState.IsOpen) or nameof(GuideWindowState.IsShown)
            or nameof(GuideWindowState.IsHiddenByUser) or nameof(GuideWindowState.IsHiddenByGame)
            or nameof(GuideWindowState.IsHiddenByTask) or nameof(GuideWindowState.IsClickThrough))
        {
            UpdateStatus();
        }
    }

    /// <summary>
    /// 状态说明
    /// </summary>
    private void UpdateStatus()
    {
        var t = I18nService.Instance;
        if (!IsAvailable)
        {
            StatusText = t.Translate("悬浮小窗只能在主实例中使用");
            return;
        }

        StatusText = !State.IsOpen
            ? t.Translate("小窗未打开")
            : State.IsShown
                ? State.IsClickThrough ? t.Translate("正在显示 · 鼠标穿透中") : t.Translate("正在显示")
                : State.IsHiddenByTask
                    ? t.Translate("已隐藏：任务运行中，结束后自动恢复")
                    : State.IsHiddenByGame
                        ? t.Translate("已隐藏：游戏不在前台，回到游戏后自动显示")
                        : t.Translate("已隐藏");
    }

    /// <summary>
    /// 快捷键摘要
    /// </summary>
    private void UpdateHotKeySummary()
    {
        var bound = HotKeys.Count(hotKey => !hotKey.HotKey.IsEmpty);
        HotKeySummary = string.Format(I18nService.Instance.Translate("已绑定 {0} / {1}"), bound, HotKeys.Count);
    }

    #endregion
}
