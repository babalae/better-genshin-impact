using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Domain;
using BetterGenshinImpact.GameTask.QuestVoice.Genshin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice.Features.PreSynthesis;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>双语节点显示行，状态由实际 P1 请求更新。</summary>
public partial class QuestVoiceDialogueRow(DialogueMapping mapping, string speaker, string chinese, string japanese)
    : ObservableObject
{
    /// <summary>来源节点身份，供结果与剧情选择关联。</summary>
    public DialogueMapping Mapping { get; } = mapping;
    /// <summary>来源图中唯一节点键。</summary>
    public string Key => Mapping.Chs?.Key ?? Mapping.Jp!.Key;
    /// <summary>发言角色。</summary>
    public string Speaker { get; } = speaker;
    /// <summary>简体中文台词。</summary>
    public string Chinese { get; } = chinese;
    /// <summary>日文台词。</summary>
    public string Japanese { get; } = japanese;
    /// <summary>只有结构一致且唯一的双语对白才允许预合成。</summary>
    public bool CanSynthesize => Mapping.Status == MappingStatus.Matched && !string.IsNullOrWhiteSpace(Chinese) && !string.IsNullOrWhiteSpace(Japanese);
    /// <summary>逐项合成结果。</summary>
    [ObservableProperty] private string _state = mapping.Status == MappingStatus.Matched ? "待生成" : mapping.Status.ToString();
}

/// <summary>
/// BetterGI 原生设置窗口中的全局剧情状态；只加载 Yatta 正式数据，不接入任何视频测试入口。
/// </summary>
public partial class QuestVoiceStoryWorkspace : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly QuestVoiceSessionFactory _factory;
    private readonly QuestVoiceConfig _config;
    private readonly QuestVoicePreSynthesisService _preSynthesis;
    private readonly QuestDataLoader _loader;
    private readonly YattaQuestDataSource _source = new();
    private readonly string _catalogPath;
    private CancellationTokenSource? _selectionCancellation;
    private CancellationTokenSource? _batchCancellation;
    private bool _catalogLoaded;
    private DialogueLanguageMap? _pair;
    private bool _pauseRequested;
    /// <summary>只播放缓存 WAV 的界面预览播放器，不影响实时 WASAPI 播放会话。</summary>
    private readonly MediaPlayer _cachedPreview = new();

    /// <summary>单一全局配置，同步角色与引擎页面。</summary>
    public QuestVoiceConfig Config => _config;
    /// <summary>旅行者选择：0 空，1 荧；映射既有配置，不新增保存字段。</summary>
    public int TravelerChoiceIndex
    {
        get => _config.FemaleTraveler ? 1 : 0;
        set
        {
            if (IsGenerating || value is not (0 or 1)) return;
            _config.FemaleTraveler = value == 1;
            OnPropertyChanged();
        }
    }
    /// <summary>剧情页与角色管理页共享同一编辑草稿和自动保存，不复制声线档案。</summary>
    public QuestVoiceManagementViewModel? Management { get; set; }
    /// <summary>全局章节目录。</summary>
    public ObservableCollection<YattaChapter> Chapters { get; } = [];
    /// <summary>当前章节的中日双语节点，列表容器负责虚拟化。</summary>
    public ObservableCollection<QuestVoiceDialogueRow> Lines { get; private set; } = [];
    /// <summary>搜索仅筛选视图，不改变全局剧情选择。</summary>
    public IEnumerable<YattaChapter> FilteredChapters => string.IsNullOrWhiteSpace(ChapterSearch)
        ? Chapters : Chapters.Where(c => c.Label.Contains(ChapterSearch.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>章节列表搜索文本。</summary>
    [ObservableProperty] private string _chapterSearch = "";
    /// <summary>当前全局剧情章节。</summary>
    [ObservableProperty] private YattaChapter? _selectedChapter;
    /// <summary>当前聚焦的台词。</summary>
    [ObservableProperty] private QuestVoiceDialogueRow? _selectedLine;
    /// <summary>目录与台词加载诊断。</summary>
    [ObservableProperty] private string _catalogStatus = "未载入剧情目录";
    /// <summary>当前章节状态。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasChapterError))] private string _chapterStatus = "请从左侧选择剧情";
    /// <summary>正常的章节计数与进度不占用界面；只对读取失败显示重试入口。</summary>
    public bool HasChapterError => ChapterStatus.StartsWith("系列加载失败", StringComparison.Ordinal)
        || ChapterStatus.StartsWith("剧情目录加载失败", StringComparison.Ordinal)
        || ChapterStatus.StartsWith("无法重试", StringComparison.Ordinal)
        || ChapterStatus.StartsWith("章节目录已更新", StringComparison.Ordinal);

    /// <summary>所选节点前后上下文。</summary>
    [ObservableProperty] private string _focusContext = "选择任意对白查看前后关系";
    /// <summary>P1 队列进度文本。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPreSynthesisActivity))] private string _preSynthesisStatus = "尚未开始预合成";
    /// <summary>已经完成的条数（包括缓存命中）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CacheHitRate))] [NotifyPropertyChangedFor(nameof(HasPreSynthesisActivity))] private int _completed;
    /// <summary>需要处理的总条数。</summary>
    [ObservableProperty] private int _total;
    /// <summary>已复用的 WAV 数量。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CacheHitRate))] private int _cacheHits;
    /// <summary>生成失败的条数。</summary>
    [ObservableProperty] private int _failed;
    /// <summary>后台是否正在运行。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditable))] [NotifyPropertyChangedFor(nameof(HasPreSynthesisActivity))] [NotifyPropertyChangedFor(nameof(CanGenerateSelectedStory))]
    private bool _isGenerating;
    /// <summary>预合成时全局章节与语种不允许被改写。</summary>
    public bool IsEditable => !IsGenerating;
    /// <summary>没有开始生成时隐藏冗余进度条；运行、完成、失败后才显示。</summary>
    public bool HasPreSynthesisActivity => IsGenerating || Completed > 0 || Failed > 0
        || PreSynthesisStatus.StartsWith("预合成已中断", StringComparison.Ordinal)
        || PreSynthesisStatus.StartsWith("已停止", StringComparison.Ordinal);
    /// <summary>用于右侧资源操作区。</summary>
    public string CacheDirectory => _preSynthesis.CacheDirectory;
    /// <summary>只统计本模块生成的 WAV，不扫描模型、视频或其他工具缓存。</summary>
    public string CacheSummary
    {
        get
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return "WAV 缓存：0 条 · 0 MiB";
                var files = Directory.EnumerateFiles(CacheDirectory, "*.wav").Select(path => new FileInfo(path)).ToArray();
                return $"WAV 缓存：{files.Length} 条 · {files.Sum(file => file.Length) / 1048576d:F1} MiB";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { return "WAV 缓存统计失败：" + error.Message; }
        }
    }
    /// <summary>UI 显示进度，避免除以零。</summary>
    public double ProgressPercent => Total == 0 ? 0 : Math.Clamp(100d * Completed / Total, 0, 100);
    /// <summary>已完成项中来自有效 WAV 缓存的百分比。</summary>
    public double CacheHitRate => Completed == 0 ? 0 : 100d * CacheHits / Completed;

    /// <summary>复用 BetterGI 的后台服务及配置，不新增独立 TTS 账号/设置。</summary>
    public QuestVoiceStoryWorkspace(QuestVoiceSessionFactory factory, QuestVoiceConfig config, QuestVoiceService live)
    {
        _factory = factory;
        _config = config;
        _preSynthesis = new(factory, config, live);
        _catalogPath = Path.Combine(factory.ProgramDirectory, "User", "QuestVoice", "cache", "catalog-chs.json");
        _loader = new QuestDataLoader(_source, Http, new QuestJSONStore(Path.Combine(factory.ProgramDirectory, "User", "QuestVoice"), "cache/quests"));
        config.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(QuestVoiceConfig.SpeechLanguage) or
                nameof(QuestVoiceConfig.TravelerNickname) or nameof(QuestVoiceConfig.FemaleTraveler))
                if (!IsGenerating) RebuildLines();
            if (args.PropertyName == nameof(QuestVoiceConfig.FemaleTraveler))
                OnPropertyChanged(nameof(TravelerChoiceIndex));
        };
    }

    /// <summary>搜索时无需发网络请求。</summary>
    partial void OnChapterSearchChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredChapters));
        RefreshCatalogRows();
    }

    /// <summary>章节切换只发生于本次的一个全局选择。</summary>
    partial void OnSelectedChapterChanged(YattaChapter? value)
    {
        if (value is null || IsGenerating || _selectingInternally) return;
        _config.SelectedChapterId = value.Id;
        _config.SelectedChapterIds = [value.Id];
        Completed = 0; CacheHits = 0; Failed = 0; Total = 0;
        OnPropertyChanged(nameof(ProgressPercent));
        PreSynthesisStatus = "已切换剧情；可预合成当前选择范围。";
        // 列表选择回调期间不立刻重建 ItemsSource，避免 WPF 选择同步递归；载入完成再刷新目录树。
        _ = LoadSelectionAsync([value], value.Title);
    }

    /// <summary>只使用 Yatta 的实际边计算前后关联；相邻列表行不代表剧情图上相邻。</summary>
    partial void OnSelectedLineChanged(QuestVoiceDialogueRow? value)
    {
        if (value?.Mapping.Chs is not { } node || _loadedPairs.Count == 0)
        {
            FocusContext = "选择任意对白查看前置与后续节点";
            return;
        }

        var document = _loadedPairs.FirstOrDefault(pair => pair.Chs.ChapterId == node.ChapterId)?.Chs;
        if (document is null) { FocusContext = "无法定位此章节的关联图"; return; }
        string Describe(string? key) => Lines.FirstOrDefault(line => line.Key == key) is { } row
            ? row.Speaker + "：" + row.Chinese
            : "中间包含结构节点或尚未展开的分支";
        var previous = document.Edges.Where(edge => edge.TargetKey == node.Key)
            .Select(edge => Describe(edge.FromKey)).Distinct().Take(3).ToArray();
        var following = document.Edges.Where(edge => edge.FromKey == node.Key)
            .Select(edge => Describe(edge.TargetKey)).Distinct().Take(3).ToArray();
        FocusContext = "前置：" + (previous.Length > 0 ? string.Join(" / ", previous) : "本任务无直接前置") +
            "  →  当前：" + value.Chinese +
            "  →  后续：" + (following.Length > 0 ? string.Join(" / ", following) : "本任务无直接后续");
    }

    /// <summary>首次打开设置窗口只加载一次目录；失败可手动刷新。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_catalogLoaded) await LoadCatalogAsync(false);
        if (Management is not null)
        {
            await Management.EnsureLoadedAsync();
            Management.SetCurrentStorySpeakers(Lines.Select(line => line.Speaker), _config.SpeechLanguage);
        }
    }

    /// <summary>手动强制向 Yatta 请求最新章节目录。</summary>
    [RelayCommand]
    private Task RefreshCatalogAsync() => LoadCatalogAsync(true);

    /// <summary>先用已验证缓存，失败再联网；网络异常时保留已经有的目录。</summary>
    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        CatalogStatus = "正在加载原神剧情目录…";
        try
        {
            byte[] bytes;
            bool cache = !forceRefresh && File.Exists(_catalogPath);
            if (cache)
            {
                try
                {
                    bytes = await File.ReadAllBytesAsync(_catalogPath);
                    _ = YattaQuestCatalogReader.Read(bytes);
                }
                catch (Exception) { cache = false; bytes = []; }
            }
            else bytes = [];

            if (!cache)
            {
                using var response = await Http.GetAsync(_source.GetCatalogUri("chs"));
                response.EnsureSuccessStatusCode();
                bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("剧情目录超过 16 MiB。");
                _ = YattaQuestCatalogReader.Read(bytes);
                Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
                var temporary = _catalogPath + "." + Guid.NewGuid().ToString("N");
                try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, _catalogPath, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }

            var records = YattaQuestCatalogReader.Read(bytes);
            ReadVerifiedEmptyChapters(bytes);
            Chapters.Clear();
            foreach (var chapter in records.OrderBy(x => x.Title, StringComparer.CurrentCulture))
                Chapters.Add(chapter);
            OnPropertyChanged(nameof(FilteredChapters));
            _catalogLoaded = true;
            CatalogStatus = $"{Chapters.Count} 章 · " + (cache ? "本地缓存" : "Yatta 最新目录");
            OnCatalogLoaded();
        }
        catch (Exception error)
        {
            CatalogStatus = "剧情目录加载失败：" + error.Message + "；可重试，已加载的目录会保留。";
        }
    }

    /// <summary>
    /// Loads the full selected Yatta series using the same document loader and cache as the single chapter.
    /// Old selections are cancelled; a partial/late series cannot silently overwrite the newest selection.
    /// </summary>
    private async Task LoadSelectionAsync(IReadOnlyList<YattaChapter> chapters, string selectionName)
    {
        _selectionCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _selectionCancellation = cts;
        // 先通知右侧开始等待，再清空上一次的角色；避免网络请求时看起来像没响应。
        StoryLoadFailure = "";
        StoryLoadingText = $"正在加载 {selectionName} 的剧情角色…";
        IsStoryLoading = true;
        ChapterStatus = $"准备 {chapters.Count} 章的 CHS / JP 双语脚本…";
        _loadedPairs.Clear();
        _pair = null;
        Lines = [];
        TranscriptRows = [];
        OnPropertyChanged(nameof(Lines));
        OnPropertyChanged(nameof(TranscriptRows));
        OnPropertyChanged(nameof(VisibleLines));
        Management?.SetCurrentStorySpeakers([], _config.SpeechLanguage);
        var loaded = new List<DialogueLanguageMap>();
        try
        {
            for (var index = 0; index < chapters.Count; index++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var chapter = chapters[index];
                StoryLoadingText = $"正在加载 {selectionName} · 第 {index + 1}/{chapters.Count} 章";
                ChapterStatus = $"正在读取 {selectionName} · {index + 1}/{chapters.Count} 章：{chapter.Title}";
                var pair = await QuestLanguagePairLoader.LoadAsync(_loader, chapter.Id,
                    cancellationToken: cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_selectionCancellation, cts)) return;
                loaded.Add(pair);
            }
            if (!ReferenceEquals(_selectionCancellation, cts)) return;
            // A chapter with only empty dialogue/option fields should not occupy the reading
            // list, the role list or the P1 synthesis queue. Full raw cache remains intact.
            var readable = loaded.Where(HasReadableChapter).ToArray();
            var newEmpty = false;
            foreach (var empty in loaded.Except(readable))
                newEmpty |= _verifiedEmptyChapters.Add(empty.Chs.ChapterId);
            if (newEmpty) SaveVerifiedEmptyChapters();
            _loadedPairs.AddRange(readable);
            _pair = readable.FirstOrDefault();
            if (readable.Length == 0)
            {
                StoryLoadFailure = "所选剧情没有可阅读的对白或对话选项。";
                ChapterStatus = "系列加载失败：" + StoryLoadFailure;
                RefreshCatalogRows();
                return;
            }
            RebuildLines();
            RefreshCatalogRows();
            StoryLoadFailure = "";
            ChapterStatus = $"{selectionName} · {_loadedPairs.Count} 章 · {Lines.Count} 条对白 · {Lines.Count(x => x.CanSynthesize)} 条严格双语配对";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_selectionCancellation, cts))
            {
                StoryLoadFailure = cts.IsCancellationRequested
                    ? "加载已取消，可以重试。"
                    : "剧情数据源响应超时，请检查网络连接或代理设置后重试。";
                ChapterStatus = "系列加载失败：" + StoryLoadFailure;
            }
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_selectionCancellation, cts))
            {
                StoryLoadFailure = error is HttpRequestException
                    ? "无法连接剧情数据源（Yatta），请检查网络或代理后重试。\n" + error.Message
                    : "读取剧情数据失败，请重试。\n" + error.Message;
                ChapterStatus = "系列加载失败：" + StoryLoadFailure;
            }
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cts))
            {
                // 不允许旧请求的 finally 覆盖新选中的系列的转圈状态。
                IsStoryLoading = false;
                NotifyStoryDisplayChanged();
                _selectionCancellation = null;
            }
            cts.Dispose();
        }
    }
    /// <summary>重新渲染旅行者昵称/性别模板，不修改来源 Yatta 文本。</summary>
    private void RebuildLines()
    {
        if (_loadedPairs.Count == 0 || IsGenerating) return;
        var selectedKey = SelectedLine?.Key;
        var context = new QuestTextContext(_config.TravelerNickname,
            _config.FemaleTraveler ? TravelerGender.Female : TravelerGender.Male);
        var updated = new List<QuestVoiceDialogueRow>();
        foreach (var pair in _loadedPairs)
            foreach (var mapping in pair.Entries.Where(e => e.Chs is { Type: "SingleDialog" }))
            {
                var cn = mapping.Chs!;
                string Render(QuestNode? n) => n?.Texts.FirstOrDefault()?.Template.Render(context)?.Trim() ?? "";
                var chinese = Render(cn);
                var japanese = Render(mapping.Jp);
                if (!string.IsNullOrWhiteSpace(chinese) || !string.IsNullOrWhiteSpace(japanese))
                    updated.Add(new(mapping, cn.Role, chinese, japanese));
            }
        Lines = new ObservableCollection<QuestVoiceDialogueRow>(updated);
        OnPropertyChanged(nameof(Lines));
        RebuildTranscript();        SelectedLine = Lines.FirstOrDefault(x => x.Key == selectedKey);
        Management?.SetCurrentStorySpeakers(Lines.Select(line => line.Speaker), _config.SpeechLanguage);
        UpdateCurrentScope();
    }

    /// <summary>以全局章节为唯一来源生成所有已确认的双语对白。</summary>
    [RelayCommand]
    private async Task StartPreSynthesisAsync()
    {
        if (!CanGenerateSelectedStory) { PreSynthesisStatus = "请先成功加载完整剧情系列的双语对白。"; return; }
        var entries = Lines.Where(x => x.CanSynthesize).ToArray();
        if (entries.Length == 0) { PreSynthesisStatus = "当前章节没有可安全配对的对白。"; return; }
        var requests = entries.Select(x => new QuestPreSynthesisLine(x.Key, x.Speaker,
            _config.SpeechLanguage == "jpn" ? x.Japanese : x.Chinese)).ToArray();
        _batchCancellation = new CancellationTokenSource();
        _pauseRequested = false;
        IsGenerating = true;
        Completed = CacheHits = Failed = 0; Total = requests.Length;
        OnPropertyChanged(nameof(ProgressPercent));
        PreSynthesisStatus = "P1 已启动，实时 P0 优先；当前范围：" + ActiveSelectionLabel;
        var progress = new Progress<QuestPreSynthesisProgress>(update =>
        {
            var row = Lines.FirstOrDefault(x => x.Key == update.Key);
            if (row is not null) row.State = update.State;
            Completed = update.Completed; Total = update.Total;
            CacheHits = update.CacheHits; Failed = update.Failed;
            OnPropertyChanged(nameof(ProgressPercent));
            PreSynthesisStatus = $"P1 · {Completed}/{Total} · 缓存 {CacheHits} · 失败 {Failed} · {update.State}";
        });
        try
        {
            var result = await _preSynthesis.GenerateAsync(requests, _config.SpeechLanguage,
                async token => { while (_pauseRequested) await Task.Delay(200, token); },
                progress, _batchCancellation.Token);
            PreSynthesisStatus = $"完成 {result.Completed}/{result.Total}，缓存命中 {result.CacheHits}，失败 {result.Failed}。";
        }
        catch (OperationCanceledException) { PreSynthesisStatus = $"已停止，保留 {Completed} 条完成的 WAV。"; }
        catch (Exception error) { PreSynthesisStatus = "预合成已中断：" + error.Message; }
        finally
        {
            IsGenerating = false;
            OnPropertyChanged(nameof(CacheSummary));
            _batchCancellation.Dispose();
            _batchCancellation = null;
            _pauseRequested = false;
        }
    }

    /// <summary>播放本地缓存的 WAV，未生成时清晰提示而不是重新请求在线 TTS。</summary>
    [RelayCommand]
    private void PlayCachedWAV()
    {
        _cachedPreview.Close();
        if (SelectedLine is not { } selected)
        {
            PreSynthesisStatus = "请先选择要播放的对白。";
            return;
        }
        var path = _preSynthesis.FindCachedWAV(selected.Key, _config.SpeechLanguage);
        if (path is null)
        {
            PreSynthesisStatus = "当前台词没有已生成的本地 WAV，需先执行预合成。";
            return;
        }
        try
        {
            _cachedPreview.Open(new Uri(path));
            _cachedPreview.Play();
            PreSynthesisStatus = "正在播放本地 WAV：" + selected.Speaker;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            PreSynthesisStatus = "播放失败：" + error.Message;
        }
    }

    /// <summary>停止当前本地试听，不影响任何正在生成的配音。</summary>
    [RelayCommand]
    private void StopCachedWAV()
    {
        _cachedPreview.Close();
        PreSynthesisStatus = "已停止本地 WAV 试听。";
    }

    /// <summary>在资源管理器中打开预合成缓存目录，用户运行时仍可手工查看 WAV。</summary>
    [RelayCommand]
    private void OpenCacheDirectory()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", CacheDirectory) { UseShellExecute = true });
        }
        catch (Exception error) { PreSynthesisStatus = "打开缓存目录失败：" + error.Message; }
    }

    /// <summary>用户确认后只清理本模块可恢复的 WAV 和节点索引，保留其余声线与剧情配置。</summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (IsGenerating) { PreSynthesisStatus = "请先停止预合成，不能清理正在写入的缓存。"; return; }
        if (!Directory.Exists(CacheDirectory)) { PreSynthesisStatus = "缓存目录为空。"; return; }
        if ((new DirectoryInfo(CacheDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            PreSynthesisStatus = "缓存目录为链接路径，已拒绝自动清理。";
            return;
        }
        if (await ThemedMessageBox.QuestionAsync(
                "确定清理 QuestVoice 预合成 WAV 缓存吗？这些音频可重新生成；其他配置和模型不会删除。",
                "清理预合成缓存") != MessageBoxResult.Yes) return;

        try
        {
            _cachedPreview.Close();
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*.wav"))
            {
                File.Delete(file);
                count++;
            }
            var index = Path.Combine(CacheDirectory, "line-index.json");
            if (File.Exists(index)) File.Delete(index);
            OnPropertyChanged(nameof(CacheSummary));
            PreSynthesisStatus = $"已清理 {count} 个生成 WAV，保留全部角色映射和剧情数据。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            OnPropertyChanged(nameof(CacheSummary));
            PreSynthesisStatus = "部分缓存清理失败：" + error.Message;
        }
    }

    /// <summary>暂停新的语音任务；当前已经发送的 HTTP 请求继续安全排空。</summary>
    [RelayCommand] private void PausePreSynthesis() { _pauseRequested = true; PreSynthesisStatus = "暂停中：当前请求完成后暂停"; }
    /// <summary>恢复剩余的 P1 请求。</summary>
    [RelayCommand] private void ResumePreSynthesis() { _pauseRequested = false; PreSynthesisStatus = "继续生成剩余台词"; }
    /// <summary>取消未来请求，不在切换权重时强制中断后端。</summary>
    [RelayCommand] private void StopPreSynthesis() { _batchCancellation?.Cancel(); PreSynthesisStatus = "停止请求已收到，当前合成完成后退出"; }
}
