using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MusicPageViewModel : ViewModel
{
    private readonly IMusicLibraryService _libraryService;
    private readonly IMusicPlaybackService _playbackService;
    private readonly IInstrumentProfileService _profileService;
    private readonly IMusicTimelineBuilder _timelineBuilder;
    private readonly IMusicStateStore _stateStore;
    private readonly ISnackbarService _snackbarService;
    private readonly ILogger<MusicPageViewModel> _logger = App.GetLogger<MusicPageViewModel>();
    private CancellationTokenSource? _refreshCancellationTokenSource;
    private Task? _sessionTask;
    private bool _isLoading;
    private bool _isUpdatingSelection;
    private bool _isInitialized;

    public MusicPageViewModel(
        IMusicLibraryService libraryService,
        IMusicPlaybackService playbackService,
        IInstrumentProfileService profileService,
        IMusicTimelineBuilder timelineBuilder,
        IMusicStateStore stateStore,
        IConfigService configService,
        ISnackbarService snackbarService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _profileService = profileService;
        _timelineBuilder = timelineBuilder;
        _stateStore = stateStore;
        _snackbarService = snackbarService;
        Config = configService.Get();
        MusicFolderDisplayText = string.IsNullOrWhiteSpace(Config.MusicConfig.MusicFolder)
            ? "尚未选择曲谱目录"
            : Config.MusicConfig.MusicFolder;

        MusicItemsView = CollectionViewSource.GetDefaultView(MusicItems);
        MusicItemsView.Filter = FilterMusicItem;
        MusicItems.CollectionChanged += OnMusicItemsCollectionChanged;

        Profiles = profileService.Profiles;
        MappingModes = new ObservableCollection<InstrumentMappingMode>(
            Enum.GetValues<InstrumentMappingMode>());
        UseBackgroundInput = Config.MusicConfig.InputMode != nameof(MusicInputMode.ForegroundSendInput);
        PlaybackMode = Config.MusicConfig.PlaybackMode switch
        {
            nameof(MusicPlaybackMode.SingleLoop) => MusicPlaybackMode.SingleLoop,
            nameof(MusicPlaybackMode.Shuffle) => MusicPlaybackMode.Shuffle,
            _ => MusicPlaybackMode.Sequential
        };

        _libraryService.FilesChanged += OnLibraryFilesChanged;
        _playbackService.SnapshotChanged += OnPlaybackSnapshotChanged;
    }

    public AllConfig Config { get; }

    public ObservableCollection<PerformanceScore> MusicItems { get; } = [];

    public ICollectionView MusicItemsView { get; }

    public ObservableCollection<InstrumentProfile> Profiles { get; }

    public ObservableCollection<InstrumentMappingMode> MappingModes { get; }

    public ObservableCollection<string> FormatFilters { get; } = ["全部格式"];

    public ObservableCollection<string> InstrumentFilters { get; } = ["全部乐器"];

    [ObservableProperty]
    private PerformanceScore? _selectedMusicItem;

    [ObservableProperty]
    private InstrumentProfile? _selectedInstrumentProfile;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFormatFilter = "全部格式";

    [ObservableProperty]
    private string _selectedInstrumentFilter = "全部乐器";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputModeDisplayText))]
    [NotifyPropertyChangedFor(nameof(InputModeToolTip))]
    private bool _useBackgroundInput = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackModeSymbol))]
    [NotifyPropertyChangedFor(nameof(PlaybackModeToolTip))]
    private MusicPlaybackMode _playbackMode;

    [ObservableProperty]
    private int _transpose;

    [ObservableProperty]
    private double _currentPositionMilliseconds;

    [ObservableProperty]
    private double _totalDurationMilliseconds = 1;

    [ObservableProperty]
    private string _currentTimeText = "00:00";

    [ObservableProperty]
    private string _totalTimeText = "00:00";

    [ObservableProperty]
    private string _currentTrackName = "未播放";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasMusicItems;

    [ObservableProperty]
    private string _libraryStatusText = "尚未扫描曲谱目录";

    [ObservableProperty]
    private string _lastRefreshText = "等待刷新";

    [ObservableProperty]
    private string _musicFolderDisplayText = "尚未选择曲谱目录";

    public SymbolRegular PlayPauseSymbol => IsPlaying
        ? SymbolRegular.Pause24
        : SymbolRegular.Play24;

    public SymbolRegular PlaybackModeSymbol => PlaybackMode switch
    {
        MusicPlaybackMode.SingleLoop => SymbolRegular.ArrowRepeatAll24,
        MusicPlaybackMode.Shuffle => SymbolRegular.ArrowShuffle24,
        _ => SymbolRegular.ArrowWrap20
    };

    public string PlaybackModeToolTip => $"播放模式：{GetPlaybackModeText(PlaybackMode)}（点击切换）";

    public string InputModeDisplayText => UseBackgroundInput ? "后台" : "前台";

    public string InputModeToolTip => UseBackgroundInput
        ? "后台 PostMessage：会继续向已关联的游戏窗口发键。"
        : "前台 SendInput：游戏需要保持前台。";

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseSymbol));
    }

    public override Task OnNavigatedToAsync()
    {
        return InitializeAsync();
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            _libraryService.Watch(Config.MusicConfig.MusicFolder);
            // 先让导航完成布局和首帧渲染，再开始读取曲库，避免页面切换看起来卡死。
            await Application.Current.Dispatcher.InvokeAsync(
                static () => { },
                DispatcherPriority.ContextIdle);
            await RefreshLibraryAsync();
        }
    }

    partial void OnSelectedMusicItemChanged(PerformanceScore? value)
    {
        if (value == null)
        {
            return;
        }

        _isUpdatingSelection = true;
        var preferredProfile = string.IsNullOrWhiteSpace(value.OutputProfileName)
            ? _profileService.Find(value.Instrument)
            : _profileService.Find(value.OutputProfileName);
        SelectedInstrumentProfile = preferredProfile;
        value.OutputProfileName = preferredProfile.Name;
        Transpose = value.Transpose;
        _isUpdatingSelection = false;
        RefreshMapping();
    }

    partial void OnSelectedInstrumentProfileChanged(InstrumentProfile? value)
    {
        if (_isUpdatingSelection || value == null || SelectedMusicItem == null)
        {
            return;
        }

        SelectedMusicItem.OutputProfileName = value.Name;
        Config.MusicConfig.SelectedInstrumentProfile = value.Name;
        SaveSelectedPreference();
        RefreshMapping();
    }

    partial void OnTransposeChanged(int value)
    {
        if (_isUpdatingSelection || SelectedMusicItem == null)
        {
            return;
        }

        SelectedMusicItem.Transpose = Math.Clamp(value, -24, 24);
        SaveSelectedPreference();
        RefreshMapping();
    }

    partial void OnSearchTextChanged(string value)
    {
        MusicItemsView.Refresh();
    }

    partial void OnSelectedFormatFilterChanged(string value)
    {
        MusicItemsView.Refresh();
    }

    partial void OnSelectedInstrumentFilterChanged(string value)
    {
        MusicItemsView.Refresh();
    }

    partial void OnUseBackgroundInputChanged(bool value)
    {
        Config.MusicConfig.InputMode = GetInputMode().ToString();
    }

    partial void OnPlaybackModeChanged(MusicPlaybackMode value)
    {
        Config.MusicConfig.PlaybackMode = value.ToString();
        _playbackService.SetPlaybackMode(value);
    }

    [RelayCommand]
    private void CyclePlaybackMode()
    {
        PlaybackMode = PlaybackMode switch
        {
            MusicPlaybackMode.Sequential => MusicPlaybackMode.SingleLoop,
            MusicPlaybackMode.SingleLoop => MusicPlaybackMode.Shuffle,
            _ => MusicPlaybackMode.Sequential
        };
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = "选择曲谱根目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(Config.MusicConfig.MusicFolder)
                ? Config.MusicConfig.MusicFolder
                : string.Empty
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Config.MusicConfig.MusicFolder = dialog.SelectedPath;
        MusicFolderDisplayText = dialog.SelectedPath;
        _libraryService.Watch(dialog.SelectedPath);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!Directory.Exists(Config.MusicConfig.MusicFolder))
        {
            _snackbarService.Show(
                "曲谱目录不可用",
                "请先选择一个有效的曲谱目录。",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(3));
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", Config.MusicConfig.MusicFolder)
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _libraryService.Watch(Config.MusicConfig.MusicFolder);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void PlayPause()
    {
        var snapshot = _playbackService.Snapshot;
        if (snapshot.State == MusicPlaybackState.Playing)
        {
            _playbackService.Pause();
            return;
        }

        if (snapshot.State == MusicPlaybackState.Paused)
        {
            _playbackService.Resume();
            return;
        }

        StartPlaybackSession();
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
    }

    [RelayCommand]
    private void Next()
    {
        _playbackService.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _playbackService.Previous();
    }

    [RelayCommand]
    private void Seek(double milliseconds)
    {
        _playbackService.Seek(TimeSpan.FromMilliseconds(
            Math.Clamp(milliseconds, 0, TotalDurationMilliseconds)));
    }

    [RelayCommand]
    private void RefreshMapping()
    {
        if (SelectedMusicItem == null || SelectedInstrumentProfile == null)
        {
            return;
        }

        _timelineBuilder.Build(SelectedMusicItem, SelectedInstrumentProfile, Transpose);
    }

    [RelayCommand]
    private void UpdateTrackSelection()
    {
        RefreshMapping();
        SaveSelectedPreference();
    }

    [RelayCommand]
    private void SaveProfiles()
    {
        _profileService.Save();
        RefreshMapping();
        _snackbarService.Show(
            "乐器档案已保存",
            "新的音高映射将在下次开始曲目时生效。",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2));
    }

    private async Task RefreshLibraryAsync()
    {
        var refreshCancellationTokenSource = new CancellationTokenSource();
        var previousRefresh = Interlocked.Exchange(
            ref _refreshCancellationTokenSource,
            refreshCancellationTokenSource);
        previousRefresh?.Cancel();
        var cancellationToken = refreshCancellationTokenSource.Token;

        IsRefreshing = true;
        LibraryStatusText = "正在扫描曲谱目录…";
        try
        {
            var scores = await _libraryService.ScanAsync(Config.MusicConfig.MusicFolder, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _isLoading = true;
            var selectedPath = SelectedMusicItem?.RelativePath;
            MusicItems.Clear();
            foreach (var score in scores)
            {
                if (string.IsNullOrWhiteSpace(score.OutputProfileName))
                {
                    score.OutputProfileName = _profileService.Find(score.Instrument).Name;
                }

                MusicItems.Add(score);
            }

            SelectedMusicItem = MusicItems.FirstOrDefault(x =>
                                    string.Equals(x.RelativePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                                ?? MusicItems.FirstOrDefault();
            RebuildFilters();
            _isLoading = false;
            SavePlaylistOrder();

            var playableCount = MusicItems.Count(x => x.IsValid);
            LibraryStatusText = Directory.Exists(Config.MusicConfig.MusicFolder)
                ? $"已载入 {MusicItems.Count} 首曲目，其中 {playableCount} 首可播放"
                : "尚未选择有效的曲谱目录";
            LastRefreshText = $"上次刷新 {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            // 新一轮刷新已经接管
        }
        catch (Exception e)
        {
            _logger.LogError(e, "刷新曲库失败：{MusicFolder}", Config.MusicConfig.MusicFolder);
            LibraryStatusText = "刷新失败，请检查曲谱目录";
            _snackbarService.Show(
                "刷新曲库失败",
                e.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(4));
        }
        finally
        {
            if (Interlocked.CompareExchange(
                    ref _refreshCancellationTokenSource,
                    null,
                    refreshCancellationTokenSource) == refreshCancellationTokenSource)
            {
                IsRefreshing = false;
            }

            _isLoading = false;
            refreshCancellationTokenSource.Dispose();
        }
    }

    private void StartPlaybackSession()
    {
        if (SelectedMusicItem is not { IsValid: true })
        {
            _snackbarService.Show(
                "无法播放",
                SelectedMusicItem?.Error ?? "请选择一首有效曲谱。",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }

        SaveAllPreferences();
        var queue = MusicItems.Where(x => x.IsValid).ToList();
        var startIndex = queue.IndexOf(SelectedMusicItem);
        if (startIndex < 0)
        {
            return;
        }

        var options = new MusicPlaybackOptions
        {
            InputMode = GetInputMode(),
            PlaybackMode = PlaybackMode
        };

        _sessionTask = new TaskRunner().RunThreadAsync(
            () => _playbackService.RunPlaylistAsync(
                queue,
                startIndex,
                options,
                CancellationContext.Instance.Cts.Token));
        _ = ObserveSessionAsync(_sessionTask);
    }

    private async Task ObserveSessionAsync(Task sessionTask)
    {
        try
        {
            await sessionTask;
        }
        catch (Exception)
        {
            // TaskRunner 已经记录并展示任务异常
        }
    }

    private bool FilterMusicItem(object item)
    {
        if (item is not PerformanceScore score)
        {
            return false;
        }

        var matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                            || score.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                            || score.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                            || score.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        var matchesFormat = string.IsNullOrWhiteSpace(SelectedFormatFilter)
                            || SelectedFormatFilter == "全部格式"
                            || score.FormatName == SelectedFormatFilter;
        var matchesInstrument = string.IsNullOrWhiteSpace(SelectedInstrumentFilter)
                                || SelectedInstrumentFilter == "全部乐器"
                                || score.Instrument.Split(',', StringSplitOptions.TrimEntries)
                                    .Contains(SelectedInstrumentFilter, StringComparer.OrdinalIgnoreCase);
        return matchesSearch && matchesFormat && matchesInstrument;
    }

    private void RebuildFilters()
    {
        var selectedFormat = SelectedFormatFilter;
        var selectedInstrument = SelectedInstrumentFilter;

        FormatFilters.Clear();
        FormatFilters.Add("全部格式");
        foreach (var format in MusicItems.Select(x => x.FormatName).Distinct().Order())
        {
            FormatFilters.Add(format);
        }

        InstrumentFilters.Clear();
        InstrumentFilters.Add("全部乐器");
        foreach (var instrument in MusicItems
                     .SelectMany(x => x.Instrument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order())
        {
            InstrumentFilters.Add(instrument);
        }

        SelectedFormatFilter = !string.IsNullOrWhiteSpace(selectedFormat)
                               && FormatFilters.Contains(selectedFormat)
            ? selectedFormat
            : "全部格式";
        SelectedInstrumentFilter = !string.IsNullOrWhiteSpace(selectedInstrument)
                                   && InstrumentFilters.Contains(selectedInstrument)
            ? selectedInstrument
            : "全部乐器";
        MusicItemsView.Refresh();
    }

    private void SaveSelectedPreference()
    {
        if (SelectedMusicItem == null)
        {
            return;
        }

        SavePreference(SelectedMusicItem);
        _stateStore.Save();
    }

    private void SaveAllPreferences()
    {
        foreach (var item in MusicItems)
        {
            SavePreference(item);
        }

        SavePlaylistOrder();
    }

    private void SavePreference(PerformanceScore score)
    {
        _stateStore.State.Items[score.RelativePath] = new MusicItemPreference
        {
            OutputProfileName = score.OutputProfileName,
            Transpose = score.Transpose,
            DisabledTrackIndexes = score.Tracks.Where(x => !x.IsEnabled).Select(x => x.Index).ToList()
        };
    }

    private void SavePlaylistOrder()
    {
        var playlistOrder = MusicItems.Select(x => x.RelativePath).ToList();
        if (_stateStore.State.PlaylistOrder.SequenceEqual(
                playlistOrder,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _stateStore.State.PlaylistOrder = playlistOrder;
        _stateStore.Save();
    }

    private void OnMusicItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasMusicItems = MusicItems.Count > 0;
        if (!_isLoading)
        {
            SavePlaylistOrder();
        }
    }

    private void OnLibraryFilesChanged(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(RefreshLibraryAsync).Task.Unwrap();
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            CurrentPositionMilliseconds = snapshot.Position.TotalMilliseconds;
            TotalDurationMilliseconds = Math.Max(1, snapshot.Duration.TotalMilliseconds);
            CurrentTimeText = FormatTime(snapshot.Position);
            TotalTimeText = FormatTime(snapshot.Duration);
            CurrentTrackName = string.IsNullOrWhiteSpace(snapshot.TrackName) ? "未播放" : snapshot.TrackName;
            IsPlaying = snapshot.State == MusicPlaybackState.Playing;
            IsPaused = snapshot.State == MusicPlaybackState.Paused;

            var queue = MusicItems.Where(x => x.IsValid).ToList();
            if (snapshot.QueueIndex >= 0
                && snapshot.QueueIndex < queue.Count
                && !ReferenceEquals(SelectedMusicItem, queue[snapshot.QueueIndex]))
            {
                SelectedMusicItem = queue[snapshot.QueueIndex];
            }
        });
    }

    private MusicInputMode GetInputMode()
    {
        return UseBackgroundInput
            ? MusicInputMode.BackgroundPostMessage
            : MusicInputMode.ForegroundSendInput;
    }

    private static string GetPlaybackModeText(MusicPlaybackMode mode)
    {
        return mode switch
        {
            MusicPlaybackMode.SingleLoop => "单曲循环",
            MusicPlaybackMode.Shuffle => "随机播放",
            _ => "顺序播放"
        };
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
