using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
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
    private readonly IMusicCoverService _coverService;
    private readonly IMusicPlaybackService _playbackService;
    private readonly IInstrumentProfileService _profileService;
    private readonly IMusicTimelineBuilder _timelineBuilder;
    private readonly IMusicStateStore _stateStore;
    private readonly ISnackbarService _snackbarService;
    private readonly ILogger<MusicPageViewModel> _logger = App.GetLogger<MusicPageViewModel>();
    private CancellationTokenSource? _refreshCancellationTokenSource;
    private CancellationTokenSource? _coverLoadCancellationTokenSource;
    private Task? _sessionTask;
    private bool _isLoading;
    private bool _isUpdatingSelection;
    private bool _isSeeking;
    private bool _isInitialized;
    private string _lastSavedPlaybackPath = string.Empty;
    private long _lastSavedPlaybackSecond = -1;

    public MusicPageViewModel(
        IMusicLibraryService libraryService,
        IMusicCoverService coverService,
        IMusicPlaybackService playbackService,
        IInstrumentProfileService profileService,
        IMusicTimelineBuilder timelineBuilder,
        IMusicStateStore stateStore,
        IConfigService configService,
        ISnackbarService snackbarService)
    {
        _libraryService = libraryService;
        _coverService = coverService;
        _playbackService = playbackService;
        _profileService = profileService;
        _timelineBuilder = timelineBuilder;
        _stateStore = stateStore;
        _snackbarService = snackbarService;
        Config = configService.Get();
        MusicFolderDisplayText = string.IsNullOrWhiteSpace(Config.MusicConfig.MusicFolder)
            ? "尚未选择曲谱目录"
            : GetFolderDisplayName(Config.MusicConfig.MusicFolder);
        MusicFolderHistory = new ObservableCollection<MusicFolderHistoryItem>(
            _stateStore.State.MusicFolderHistory
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => new MusicFolderHistoryItem(x)));
        if (!string.IsNullOrWhiteSpace(Config.MusicConfig.MusicFolder))
        {
            AddMusicFolderToHistory(Config.MusicConfig.MusicFolder);
            SelectedMusicFolder = Config.MusicConfig.MusicFolder;
        }

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
        _libraryService.ScoreParseFailed += OnScoreParseFailed;
        _playbackService.SnapshotChanged += OnPlaybackSnapshotChanged;
    }

    public AllConfig Config { get; }

    public ObservableCollection<PerformanceScore> MusicItems { get; } = [];

    public ICollectionView MusicItemsView { get; }

    public ObservableCollection<InstrumentProfile> Profiles { get; }

    public ObservableCollection<InstrumentMappingMode> MappingModes { get; }

    public ObservableCollection<MusicFolderHistoryItem> MusicFolderHistory { get; }

    public ObservableCollection<string> FormatFilters { get; } = ["全部格式"];

    public ObservableCollection<string> InstrumentFilters { get; } = ["全部乐器"];

    [ObservableProperty]
    private PerformanceScore? _selectedMusicItem;

    [ObservableProperty]
    private PerformanceScore? _currentMusicItem;

    [ObservableProperty]
    private InstrumentProfile? _selectedInstrumentProfile;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedFormatFilter = "全部格式";

    [ObservableProperty]
    private string _selectedInstrumentFilter = "全部乐器";

    [ObservableProperty]
    private string? _selectedMusicFolder;

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
        if (!_isLoading && _playbackService.Snapshot.State == MusicPlaybackState.Stopped)
        {
            CurrentMusicItem = value;
            UpdateStoppedPlaybackDisplay(value, TimeSpan.Zero);
            SavePlaybackState(value, TimeSpan.Zero, true);
        }
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

    partial void OnCurrentPositionMillisecondsChanged(double value)
    {
        if (!_isSeeking)
        {
            return;
        }

        var milliseconds = Math.Clamp(value, 0, TotalDurationMilliseconds);
        CurrentTimeText = FormatTime(TimeSpan.FromMilliseconds(milliseconds));
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

        await SelectMusicFolderAsync(dialog.SelectedPath);
    }

    [RelayCommand]
    private async Task SelectMusicFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            _snackbarService.Show(
                "曲谱目录不可用",
                "该历史目录不存在或当前无法访问。",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(3));
            return;
        }

        var normalizedFolder = Path.GetFullPath(folder);
        if (string.Equals(
                Config.MusicConfig.MusicFolder,
                normalizedFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            SelectedMusicFolder = normalizedFolder;
            return;
        }

        if (!string.Equals(
                Config.MusicConfig.MusicFolder,
                normalizedFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            _playbackService.Stop();
        }

        AddMusicFolderToHistory(normalizedFolder);
        SelectedMusicFolder = normalizedFolder;
        Config.MusicConfig.MusicFolder = normalizedFolder;
        MusicFolderDisplayText = GetFolderDisplayName(normalizedFolder);
        _libraryService.Watch(normalizedFolder);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private async Task DeleteMusicFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var historyItem = MusicFolderHistory.FirstOrDefault(x =>
            string.Equals(x.FullPath, folder, StringComparison.OrdinalIgnoreCase));
        if (historyItem == null)
        {
            return;
        }

        MusicFolderHistory.Remove(historyItem);
        SaveMusicFolderHistory();
        if (!string.Equals(
                Config.MusicConfig.MusicFolder,
                historyItem.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nextFolder = MusicFolderHistory.FirstOrDefault(x => Directory.Exists(x.FullPath))?.FullPath;
        if (nextFolder != null)
        {
            await SelectMusicFolderAsync(nextFolder);
            return;
        }

        _playbackService.Stop();
        SelectedMusicFolder = null;
        Config.MusicConfig.MusicFolder = string.Empty;
        MusicFolderDisplayText = "尚未选择曲谱目录";
        _libraryService.Watch(string.Empty);
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
    private void OpenSettings()
    {
        var window = new MusicSettingsWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
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
    private async Task PlaySelectedAsync(PerformanceScore? musicItem)
    {
        if (musicItem is not { IsValid: true })
        {
            _snackbarService.Show(
                "无法播放",
                musicItem?.Error ?? "请选择一首有效曲谱。",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }

        var currentSession = _sessionTask;
        if (_playbackService.Snapshot.State != MusicPlaybackState.Stopped)
        {
            _playbackService.Stop();
            if (currentSession != null)
            {
                await ObserveSessionAsync(currentSession);
            }
        }

        SelectedMusicItem = musicItem;
        StartPlaybackSession(false);
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
        if (SelectedMusicItem != null)
        {
            UpdateStoppedPlaybackDisplay(SelectedMusicItem, TimeSpan.Zero);
            SavePlaybackState(SelectedMusicItem, TimeSpan.Zero, true);
        }
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
    private void BeginSeek()
    {
        _isSeeking = true;
    }

    [RelayCommand]
    private void Seek(double milliseconds)
    {
        var position = TimeSpan.FromMilliseconds(
            Math.Clamp(milliseconds, 0, TotalDurationMilliseconds));
        if (_playbackService.Snapshot.State == MusicPlaybackState.Stopped)
        {
            var score = CurrentMusicItem ?? SelectedMusicItem;
            if (score != null)
            {
                UpdateStoppedPlaybackDisplay(score, position);
                SavePlaybackState(score, position, true);
            }
        }
        else
        {
            _playbackService.Seek(position);
        }

        _isSeeking = false;
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
        Volatile.Read(ref _coverLoadCancellationTokenSource)?.Cancel();
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
            var selectedPath = SelectedMusicItem?.FullPath;
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                selectedPath = _stateStore.State.CurrentTrackFullPath;
            }

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
                                    string.Equals(x.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                                ?? MusicItems.FirstOrDefault();
            RebuildFilters();
            if (_playbackService.Snapshot.State == MusicPlaybackState.Stopped)
            {
                RestorePlaybackState();
            }

            _isLoading = false;
            StartCoverLoading();

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

    private void StartCoverLoading()
    {
        var coverLoadCancellationTokenSource = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _coverLoadCancellationTokenSource,
            coverLoadCancellationTokenSource);
        previous?.Cancel();
        _ = LoadCoversAsync([.. MusicItems], coverLoadCancellationTokenSource);
    }

    private async Task LoadCoversAsync(
        IReadOnlyList<PerformanceScore> scores,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            foreach (var score in scores)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                score.Artwork = await _coverService.GetCoverAsync(
                    score.DisplayTitle,
                    cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 新一轮封面加载已经接管
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "加载音乐封面失败");
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _coverLoadCancellationTokenSource,
                null,
                cancellationTokenSource);
            cancellationTokenSource.Dispose();
        }
    }

    private void StartPlaybackSession(bool restoreSavedPosition = true)
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
            PlaybackMode = PlaybackMode,
            StartPosition = restoreSavedPosition
                ? GetSavedPlaybackPosition(SelectedMusicItem)
                : TimeSpan.Zero
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

        _stateStore.Save();
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

    private void AddMusicFolderToHistory(string folder)
    {
        var existingItem = MusicFolderHistory.FirstOrDefault(x =>
            string.Equals(x.FullPath, folder, StringComparison.OrdinalIgnoreCase));
        if (existingItem != null)
        {
            if (MusicFolderHistory.IndexOf(existingItem) == 0)
            {
                return;
            }

            MusicFolderHistory.Remove(existingItem);
        }

        MusicFolderHistory.Insert(0, new MusicFolderHistoryItem(folder));
        SaveMusicFolderHistory();
    }

    private void SaveMusicFolderHistory()
    {
        _stateStore.State.MusicFolderHistory = [.. MusicFolderHistory.Select(x => x.FullPath)];
        _stateStore.Save();
    }

    private void RestorePlaybackState()
    {
        if (SelectedMusicItem == null)
        {
            CurrentMusicItem = null;
            CurrentPositionMilliseconds = 0;
            TotalDurationMilliseconds = 1;
            CurrentTimeText = "00:00";
            TotalTimeText = "00:00";
            CurrentTrackName = "未播放";
            return;
        }

        var position = GetSavedPlaybackPosition(SelectedMusicItem);
        UpdateStoppedPlaybackDisplay(SelectedMusicItem, position);
        if (!string.Equals(
                _stateStore.State.CurrentTrackFullPath,
                SelectedMusicItem.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            SavePlaybackState(SelectedMusicItem, TimeSpan.Zero, true);
        }
    }

    private TimeSpan GetSavedPlaybackPosition(PerformanceScore score)
    {
        if (!string.Equals(
                _stateStore.State.CurrentTrackFullPath,
                score.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.Zero;
        }

        var milliseconds = _stateStore.State.CurrentPositionMilliseconds;
        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, score.Duration.TotalMilliseconds));
    }

    private void UpdateStoppedPlaybackDisplay(PerformanceScore score, TimeSpan position)
    {
        CurrentMusicItem = score;
        position = position > score.Duration ? score.Duration : position;
        CurrentPositionMilliseconds = position.TotalMilliseconds;
        TotalDurationMilliseconds = Math.Max(1, score.Duration.TotalMilliseconds);
        CurrentTimeText = FormatTime(position);
        TotalTimeText = FormatTime(score.Duration);
        CurrentTrackName = score.DisplayTitle;
    }

    private void SavePlaybackState(PerformanceScore score, TimeSpan position, bool force = false)
    {
        var pathChanged = !string.Equals(
            _lastSavedPlaybackPath,
            score.FullPath,
            StringComparison.OrdinalIgnoreCase);
        var playbackSecond = Math.Max(0, (long)position.TotalSeconds);

        _stateStore.State.CurrentTrackFullPath = score.FullPath;
        _stateStore.State.CurrentPositionMilliseconds = Math.Max(0, position.TotalMilliseconds);
        if (!force && !pathChanged && playbackSecond == _lastSavedPlaybackSecond)
        {
            return;
        }

        _lastSavedPlaybackPath = score.FullPath;
        _lastSavedPlaybackSecond = playbackSecond;
        _stateStore.Save();
    }

    private void OnMusicItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasMusicItems = MusicItems.Count > 0;
    }

    private void OnLibraryFilesChanged(object? sender, EventArgs e)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(RefreshLibraryAsync).Task.Unwrap();
    }

    private void OnScoreParseFailed(object? sender, MusicScoreParseFailedEventArgs e)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _snackbarService.Show(
                "曲谱解析失败",
                $"已跳过 {Path.GetFileName(e.FilePath)}：{e.ErrorMessage}",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(5));
        });
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_isSeeking)
            {
                CurrentPositionMilliseconds = snapshot.Position.TotalMilliseconds;
                CurrentTimeText = FormatTime(snapshot.Position);
            }

            TotalDurationMilliseconds = Math.Max(1, snapshot.Duration.TotalMilliseconds);
            TotalTimeText = FormatTime(snapshot.Duration);
            CurrentTrackName = string.IsNullOrWhiteSpace(snapshot.TrackName) ? "未播放" : snapshot.TrackName;
            IsPlaying = snapshot.State == MusicPlaybackState.Playing;
            IsPaused = snapshot.State == MusicPlaybackState.Paused;

            var queue = MusicItems.Where(x => x.IsValid).ToList();
            if (snapshot.QueueIndex >= 0
                && snapshot.QueueIndex < queue.Count)
            {
                var currentItem = queue[snapshot.QueueIndex];
                CurrentMusicItem = currentItem;
                if (!ReferenceEquals(SelectedMusicItem, currentItem))
                {
                    SelectedMusicItem = currentItem;
                }

                if (snapshot.State is MusicPlaybackState.Playing or MusicPlaybackState.Paused)
                {
                    SavePlaybackState(
                        currentItem,
                        snapshot.Position,
                        snapshot.State == MusicPlaybackState.Paused);
                }
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

    private static string GetFolderDisplayName(string folder)
    {
        var trimmedPath = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(folderName) ? folder : folderName;
    }
}

public sealed class MusicFolderHistoryItem(string fullPath)
{
    public string FullPath { get; } = fullPath;

    public string DisplayName
    {
        get
        {
            var trimmedPath = FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(folderName) ? FullPath : folderName;
        }
    }
}
