using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>策略选择控件的根 VM，负责目录快照、选择、录制及生命周期。</summary>
public partial class CombatStrategyPickerViewModel : ObservableObject
{
    /// <summary>共享录制服务。</summary>
    private readonly KeyMouseRecordService _recordService;
    /// <summary>主窗口页面导航服务。</summary>
    private readonly INavigationService _navigation;
    /// <summary>用户实际选用策略时通知视图写回依赖属性。</summary>
    public event Action<string>? StrategySelected;
    /// <summary>目录加载取消源。</summary>
    private CancellationTokenSource? _loadCts;
    /// <summary>搜索防抖取消源。</summary>
    private CancellationTokenSource? _searchCts;
    /// <summary>自动匹配页独立的元数据请求，切换类别时取消。</summary>
    private CancellationTokenSource? _autoMetadataCts;
    /// <summary>最新目录快照；关闭后保留。</summary>
    private CombatStrategyCatalogSnapshot? _snapshot;
    /// <summary>是否已挂接消息。</summary>
    private bool _attached;
    /// <summary>是否由本控件发起当前录制。</summary>
    private bool _ownsRecording;
    /// <summary>原始值，失效或不受支持时必须保留。</summary>
    private string? _value;
    /// <summary>打开菜单时定位当前文件；普通刷新保留浏览目录。</summary>
    private bool _locateCurrentOnRefresh;
    /// <summary>当前允许的类型。</summary>
    public CombatStrategyKind AllowedKinds { get; private set; } = CombatStrategyKind.All;
    /// <summary>解析后的当前引用。</summary>
    [ObservableProperty] private CombatStrategyRef? _current;
    /// <summary>弹出面板是否打开。</summary>
    [ObservableProperty] private bool _isOpen;
    /// <summary>是否正在扫描目录。</summary>
    [ObservableProperty] private bool _isLoading;
    /// <summary>目录加载失败提示。</summary>
    [ObservableProperty] private string _loadError = "";
    /// <summary>各类别共享的搜索文本。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>任意入口是否正在录制。</summary>
    [ObservableProperty] private bool _isRecording;
    /// <summary>宿主是否允许导航到录制管理。</summary>
    [ObservableProperty] private bool _canNavigate;
    /// <summary>一级类别列表。</summary>
    public ObservableCollection<StrategyCategoryViewModel> Categories { get; } = [];
    /// <summary>当前浏览类别，切换时不改变当前策略。</summary>
    [ObservableProperty] private StrategyCategoryViewModel? _selectedCategory;
    /// <summary>触发区的模式或类型标签。</summary>
    public string Badge => HasWarning ? "!" : Current?.Kind switch
    {
        null or CombatStrategyKind.AutoSelect or CombatStrategyKind.Combo => "自动",
        CombatStrategyKind.Txt => "txt", CombatStrategyKind.Json => "JSON", _ => "键鼠",
    };
    /// <summary>是否为自动模式，用描边标签显示。</summary>
    public bool IsAutomatic => Current?.Kind is null or CombatStrategyKind.AutoSelect or CombatStrategyKind.Combo;
    /// <summary>当前值失效、不被入口支持或连招未建树。</summary>
    public bool HasWarning => Current is { } current && (!current.Exists || (AllowedKinds & current.Kind) == 0
        || current.Kind == CombatStrategyKind.Combo && AutoComboRuntime.Session == null);
    /// <summary>当前失效值的固定警告，连招建树警告在对应类别显示。</summary>
    public string CurrentWarning => Current is { Exists: false } ? $"当前值不存在：{_value}" : Current is { } current && (AllowedKinds & current.Kind) == 0
        ? $"当前入口不支持此策略：{_value}" : "";
    /// <summary>当前录制由本控件发起，触发区显示停止操作。</summary>
    public bool IsOwnRecording => _ownsRecording && IsRecording;
    /// <summary>是否显示当前值警告。</summary>
    public bool HasCurrentWarning => CurrentWarning.Length > 0;
    /// <summary>是否显示目录读取错误。</summary>
    public bool HasLoadError => LoadError.Length > 0;
    /// <summary>搜索框是否显示清空按钮。</summary>
    public bool HasSearchText => SearchText.Length > 0;
    /// <summary>触发区显示文字。</summary>
    public string TriggerText => IsOwnRecording ? "录制中… 点击停止"
        : Current is { Exists: false } ? $"{_value}（不存在）" : Current?.DisplayName ?? "按队伍匹配";
    /// <summary>手动策略的目录部分，以次要文字色展示。</summary>
    public string TriggerDirectory => !IsAutomatic && Current is { Exists: true } && !IsOwnRecording
        && Current.DisplayName.LastIndexOf(" / ", StringComparison.Ordinal) is var index && index >= 0
            ? Current.DisplayName[..(index + 3)] : "";
    /// <summary>触发区的策略文件名或自动模式说明。</summary>
    public string TriggerName => TriggerDirectory.Length > 0 ? TriggerText[TriggerDirectory.Length..] : TriggerText;
    /// <summary>触发区完整提示。</summary>
    public string ToolTipText => $"{Current?.Value}\n{Current?.Path}\n{CombatStrategyMetadataProvider.GetCachedSummary(Current?.Path)}"
        + (HasWarning ? "\n" + (CurrentWarning.Length > 0 ? CurrentWarning : "自动连招尚未建树") : "");
    /// <summary>无障碍触发区名称。</summary>
    public string AutomationName => $"战斗策略：{Badge} {TriggerText}";
    /// <summary>当前是否为键鼠录制类别。</summary>
    public bool IsKeyMouseCategory => SelectedCategory?.Kind == CombatStrategyKind.KeyMouse;
    /// <summary>是否显示脚本仓库按钮。</summary>
    public bool ShowRepository => SelectedCategory?.Kind is CombatStrategyKind.Txt or CombatStrategyKind.Json or CombatStrategyKind.AutoSelect;
    /// <summary>录制按钮是否可用。</summary>
    public bool CanStartRecord => !IsRecording;
    /// <summary>录制页跳转只在主窗口显示。</summary>
    public bool ShowRecordManagement => IsKeyMouseCategory && CanNavigate;
    /// <summary>当前类别无匹配结果时，可直接跳转的其他类别。</summary>
    public IEnumerable<StrategyBrowserViewModel> SearchAlternatives => Categories.OfType<StrategyBrowserViewModel>()
        .Where(b => b != SelectedCategory && b.MatchCount > 0);
    /// <summary>未配置热键时的引导及录制使用建议。</summary>
    public string RecordHint => "适合站位固定的场景，例如秘境、首领。单段宏建议不超过 20 秒。"
        + (string.IsNullOrWhiteSpace(Service.ConfigService.Config?.HotKeyConfig.KeyMouseMacroRecordHotkey)
            ? " 可在热键设置中配置键鼠录制热键。" : " 可使用键鼠录制热键停止。 ");

    /// <summary>每个控件创建自己的 VM，复用全局录制与导航服务。</summary>
    public CombatStrategyPickerViewModel(KeyMouseRecordService recordService, INavigationService navigation)
    {
        _recordService = recordService;
        _navigation = navigation;
        SetAllowedKinds(CombatStrategyKind.All);
    }

    /// <summary>按入口允许的类型重建类别，关闭状态不扫描磁盘。</summary>
    public void SetAllowedKinds(CombatStrategyKind kinds)
    {
        // 入口类型变化后旧扫描结果不能再写回新类别。
        _loadCts?.Cancel();
        _autoMetadataCts?.Cancel();
        _searchCts?.Cancel();
        foreach (var category in Categories) category.CancelLoading();
        AllowedKinds = kinds;
        _snapshot = null;
        Categories.Clear();
        if (kinds.HasFlag(CombatStrategyKind.AutoSelect)) Categories.Add(new AutoMatchCategoryViewModel(Select, ShowErrors));
        if (kinds.HasFlag(CombatStrategyKind.Combo)) Categories.Add(new ComboCategoryViewModel(Select));
        foreach (var (kind, title) in new[] { (CombatStrategyKind.Txt, "文本策略"), (CombatStrategyKind.Json, "JSON 策略"), (CombatStrategyKind.KeyMouse, "键鼠录制") })
            if (kinds.HasFlag(kind)) Categories.Add(new StrategyBrowserViewModel(kind, title, Select));
        SelectedCategory = Categories.FirstOrDefault(c => c.Kind == Current?.Kind) ?? Categories.FirstOrDefault();
        NotifyTrigger();
        if (IsOpen) _ = RefreshAsync();
    }

    /// <summary>来自外部绑定的变化只更新显示，失效值不自动覆盖。</summary>
    public void SetCurrent(string? value)
    {
        _value = value;
        Current = CombatStrategyResolver.Parse(value);
        foreach (var browser in Categories.OfType<StrategyBrowserViewModel>()) browser.SetCurrent(Current.Path);
        if (Categories.OfType<AutoMatchCategoryViewModel>().FirstOrDefault() is { } auto)
        {
            // 外部切换配置时同步范围勾选，不因仅浏览某行而写回配置。
            for (var i = 0; i < auto.Scopes.Count; i++)
            {
                var scope = auto.Scopes[i];
                var isCurrent = string.Equals(scope.Value, Current.Value, StringComparison.OrdinalIgnoreCase);
                if (scope.IsCurrent != isCurrent) auto.Scopes[i] = scope with { IsCurrent = isCurrent };
            }
            auto.SelectedScope = auto.Scopes.FirstOrDefault(s => s.IsCurrent) ?? auto.Scopes.FirstOrDefault();
        }
        NotifyTrigger();
    }

    /// <summary>控件加载时成对订阅弱引用消息。</summary>
    public void Attach(bool canNavigate)
    {
        CanNavigate = canNavigate;
        IsRecording = _recordService.IsRecording;
        if (_attached) return;
        _attached = true;
        WeakReferenceMessenger.Default.Register<KeyMouseRecordStateChangedMessage>(this, static (recipient, message) =>
        {
            var vm = (CombatStrategyPickerViewModel)recipient;
            vm.IsRecording = message.IsRecording;
            if (!message.IsRecording) vm._ownsRecording = false;
            vm.NotifyTrigger();
        });
        WeakReferenceMessenger.Default.Register<KeyMouseRecordSavedMessage>(this, static (recipient, message) =>
        {
            var vm = (CombatStrategyPickerViewModel)recipient;
            if (!ReferenceEquals(message.Requester, vm)) return;
            vm.Select(CombatStrategyResolver.Parse(message.Path));
            App.GetService<ISnackbarService>()?.Show("已保存并选用", "可在录制管理中重命名", ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
        });
    }

    /// <summary>控件卸载时释放加载源与消息；录制文件仍由服务保存。</summary>
    public void Detach()
    {
        Close();
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _attached = false;
        _ownsRecording = false;
    }

    /// <summary>通过 Alt+↓ 打开选择器。</summary>
    [RelayCommand]
    private void Show() => IsOpen = true;

    /// <summary>打开时定位当前策略并加载新快照，已有列表可立即显示。</summary>
    [RelayCommand]
    private void Opened()
    {
        if (_ownsRecording && IsRecording) { _recordService.Stop(); IsOpen = false; return; }
        SetCurrent(_value);
        ClearSearch();
        SelectedCategory = Categories.FirstOrDefault(c => c.Kind == Current?.Kind) ?? Categories.FirstOrDefault();
        if (SelectedCategory is ComboCategoryViewModel combo) combo.Refresh();
        _locateCurrentOnRefresh = true;
        // 每次打开都立即启动新请求，不让上次异步命令的执行状态阻止重开。
        _ = RefreshAsync();
    }

    /// <summary>取消旧扫描后重新加载目录，失败在面板中显示并提供重试。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new();
        var ct = cts.Token;
        IsLoading = _snapshot == null;
        LoadError = "";
        try
        {
            var snapshot = await CombatStrategyCatalog.LoadAsync(AllowedKinds, ct);
            ct.ThrowIfCancellationRequested();
            ApplySnapshot(snapshot, _locateCurrentOnRefresh);
            if (SelectedCategory is AutoMatchCategoryViewModel auto) await LoadAutoMetadataAsync(auto, ct);
            _locateCurrentOnRefresh = false;
            OnPropertyChanged(nameof(SearchAlternatives));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            LoadError = "读取目录失败：" + ex.Message;
            App.GetLogger<CombatStrategyPickerViewModel>().LogWarning(ex, "读取战斗策略目录失败");
        }
        finally { if (ReferenceEquals(_loadCts, cts)) IsLoading = false; }
    }

    /// <summary>将目录快照应用到各类别；加载与展示分离，便于验证真实视图。</summary>
    public void ApplySnapshot(CombatStrategyCatalogSnapshot snapshot, bool locateCurrent = false)
    {
        _snapshot = snapshot;
        foreach (var browser in Categories.OfType<StrategyBrowserViewModel>())
        {
            if (snapshot.Roots.TryGetValue(browser.Kind, out var root))
                browser.SetRoot(root, locateCurrent && browser.Kind == Current?.Kind ? Current.Path : null);
            browser.SetCurrent(Current?.Path);
            browser.SetActive(IsOpen && ReferenceEquals(browser, SelectedCategory));
        }
        if (Categories.OfType<AutoMatchCategoryViewModel>().FirstOrDefault() is { } auto)
        {
            auto.Scopes.Clear();
            foreach (var node in snapshot.AutoMatchScopes)
            {
                var value = node.RelativePath.Length == 0 ? CombatStrategyResolver.AutoSelectName : CombatStrategyResolver.AutoScopePrefix + node.RelativePath;
                auto.Scopes.Add(new(node, string.Equals(Current?.Value, value, StringComparison.OrdinalIgnoreCase)));
            }
            auto.SelectedScope = auto.Scopes.FirstOrDefault(s => s.IsCurrent) ?? auto.Scopes.FirstOrDefault();
        }
    }

    /// <summary>关闭时取消目录、搜索和元数据读取，保留上次快照。</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        IsLoading = false;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _autoMetadataCts?.Cancel();
        _autoMetadataCts?.Dispose();
        _autoMetadataCts = null;
        foreach (var browser in Categories.OfType<StrategyBrowserViewModel>()) browser.SetActive(false);
        NotifyTrigger();
    }

    /// <summary>退出搜索或关闭面板，供 Esc 使用。</summary>
    [RelayCommand]
    private void Escape()
    {
        if (SearchText.Length > 0) ClearSearch();
        else if (SelectedCategory is StrategyBrowserViewModel { IsSearching: true } browser) browser.ApplySearch("");
        else Close();
    }

    /// <summary>在当前浏览目录打开资源管理器；目录不存在时先创建。</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var root = Global.Absolute(IsKeyMouseCategory ? @"User\KeyMouseScript" : @"User\AutoFight");
        var folder = Path.Combine(root, (SelectedCategory as StrategyBrowserViewModel)?.FolderRelativePath ?? "");
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LoadError = "打开目录失败：" + ex.Message;
        }
    }

    /// <summary>打开脚本仓库。</summary>
    [RelayCommand]
    private void OpenRepository()
    {
        Close();
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    /// <summary>从控件发起录制，结果通过请求者标识自动选中。</summary>
    [RelayCommand(CanExecute = nameof(CanStartRecord))]
    private async Task RecordAsync()
    {
        Close();
        _ownsRecording = true;
        if (!await _recordService.StartAsync("AutoFight", this)) _ownsRecording = false;
        IsRecording = _recordService.IsRecording;
        NotifyTrigger();
    }

    /// <summary>只有主窗口宿主允许跳转录制管理。</summary>
    [RelayCommand]
    private void NavigateRecord()
    {
        if (!CanNavigate) return;
        Close();
        _navigation.Navigate(typeof(KeyMouseRecordPage));
    }

    /// <summary>从空搜索结果跳到另一类别，保留搜索文本。</summary>
    [RelayCommand]
    private void SwitchCategory(StrategyCategoryViewModel? category)
    {
        if (category != null) SelectedCategory = category;
    }

    /// <summary>切换类别只改变浏览内容，不修改绑定值。</summary>
    partial void OnSelectedCategoryChanged(StrategyCategoryViewModel? value)
    {
        _autoMetadataCts?.Cancel();
        _autoMetadataCts?.Dispose();
        _autoMetadataCts = null;
        foreach (var browser in Categories.OfType<StrategyBrowserViewModel>()) browser.SetActive(IsOpen && ReferenceEquals(value, browser));
        if (value is ComboCategoryViewModel combo) combo.Refresh();
        if (value is AutoMatchCategoryViewModel auto && _loadCts != null) _ = LoadAutoMetadataAsync(auto, _loadCts.Token);
        OnPropertyChanged(nameof(IsKeyMouseCategory));
        OnPropertyChanged(nameof(ShowRepository));
        OnPropertyChanged(nameof(ShowRecordManagement));
        OnPropertyChanged(nameof(SearchAlternatives));
    }

    /// <summary>录制状态同步到所有实例的录制按钮。</summary>
    partial void OnIsRecordingChanged(bool value) { OnPropertyChanged(nameof(CanStartRecord)); RecordCommand.NotifyCanExecuteChanged(); NotifyTrigger(); }
    /// <summary>宿主导航能力变化时更新底栏。</summary>
    partial void OnCanNavigateChanged(bool value) => OnPropertyChanged(nameof(ShowRecordManagement));
    /// <summary>加载错误变化时收起或显示状态区。</summary>
    partial void OnLoadErrorChanged(string value) => OnPropertyChanged(nameof(HasLoadError));

    /// <summary>搜索使用 150ms 防抖；清空立即生效，避免延迟请求覆盖错误筛选。</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        OnPropertyChanged(nameof(HasSearchText));
        if (value.Length == 0) ApplySearch("");
        else
        {
            _searchCts = new();
            _ = ApplySearchAsync(value, _searchCts.Token);
        }
    }

    /// <summary>清空搜索和错误筛选，包括文本原本已为空的情况。</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        _searchCts?.Cancel();
        SearchText = "";
        ApplySearch("");
    }

    /// <summary>将搜索应用于所有手动类别，只操作内存目录树。</summary>
    private void ApplySearch(string text)
    {
        foreach (var browser in Categories.OfType<StrategyBrowserViewModel>()) browser.ApplySearch(text);
        OnPropertyChanged(nameof(SearchAlternatives));
    }

    /// <summary>防抖完成后仅搜索内存，不扫描目录。</summary>
    private async Task ApplySearchAsync(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            ct.ThrowIfCancellationRequested();
            ApplySearch(text);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>自动匹配页预读所选范围中的文本策略，失败数只统计本批最多 200 个。</summary>
    private async Task LoadAutoMetadataAsync(AutoMatchCategoryViewModel auto, CancellationToken ct)
    {
        _autoMetadataCts?.Cancel();
        _autoMetadataCts?.Dispose();
        _autoMetadataCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = _autoMetadataCts.Token;
        auto.MetadataStatus = "正在检查文本策略…";
        auto.ErrorSummary = "";
        try
        {
            var scope = auto.Scopes.FirstOrDefault(s => s.IsCurrent) ?? auto.Scopes.FirstOrDefault();
            if (scope == null) { auto.MetadataStatus = ""; return; }
            var files = CombatStrategyCatalog.Flatten(scope.Node).Take(200).ToArray();
            var metadata = await CombatStrategyMetadataProvider.GetAsync(files, ct);
            ct.ThrowIfCancellationRequested();
            var errors = metadata.Values.Count(m => m.Error != null);
            auto.ErrorSummary = errors > 0 ? $"{errors} 个文本策略解析失败，匹配时会被跳过" : "";
            auto.MetadataStatus = scope.Node.FileCount > 200 ? "仅检查当前范围的前 200 个文本策略。"
                : scope.Node.FileCount == 0 ? "范围内暂无文本策略，可从脚本仓库订阅。" : "";
            Categories.OfType<StrategyBrowserViewModel>().FirstOrDefault(b => b.Kind == CombatStrategyKind.Txt)?.ApplyMetadata(metadata);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) auto.MetadataStatus = "读取策略信息失败：" + ex.Message; }
    }

    /// <summary>只在用户实际选择或本控件录制成功时写回规范值。</summary>
    private void Select(CombatStrategyRef strategy)
    {
        if ((AllowedKinds & strategy.Kind) == 0) return;
        SetCurrent(strategy.Value);
        StrategySelected?.Invoke(strategy.Value);
        Close();
    }

    /// <summary>从自动匹配页切到文本类别的错误过滤。</summary>
    private void ShowErrors()
    {
        var browser = Categories.OfType<StrategyBrowserViewModel>().FirstOrDefault(b => b.Kind == CombatStrategyKind.Txt);
        if (browser == null) return;
        ClearSearch();
        SelectedCategory = browser;
        browser.ShowErrorsOnly();
    }

    /// <summary>刷新触发区的所有派生显示值。</summary>
    private void NotifyTrigger()
    {
        foreach (var property in new[] { nameof(Badge), nameof(IsAutomatic), nameof(IsOwnRecording), nameof(HasWarning), nameof(CurrentWarning), nameof(HasCurrentWarning), nameof(TriggerText), nameof(TriggerDirectory), nameof(TriggerName), nameof(ToolTipText), nameof(AutomationName) })
            OnPropertyChanged(property);
    }
}
