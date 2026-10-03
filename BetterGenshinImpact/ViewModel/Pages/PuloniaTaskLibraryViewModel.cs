using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// Pulonia 任务库页面视图模型，复用能力定义与资源索引完成搜索、预览和添加。
/// </summary>
public partial class PuloniaTaskLibraryViewModel : ViewModel
{
    /// <summary>
    /// 已注册能力的统一运行服务。
    /// </summary>
    private readonly IPuloniaTaskService _taskService;

    /// <summary>
    /// 任务库与创建弹窗共用的资源索引。
    /// </summary>
    private readonly PuloniaTaskResourceCatalog _resourceCatalog;

    /// <summary>
    /// 当前任务计划编辑会话，用于原子插入确认后的任务。
    /// </summary>
    private readonly PuloniaTaskPlanViewModel _taskPlanViewModel;

    /// <summary>
    /// 页面间工具入口的导航服务。
    /// </summary>
    private readonly INavigationService _navigationService;

    /// <summary>
    /// 尚未过滤的全部任务库项目。
    /// </summary>
    private readonly ObservableCollection<PuloniaTaskLibraryItemViewModel> _allItems = [];

    /// <summary>
    /// 防止较早的异步预览覆盖后来选择。
    /// </summary>
    private int _previewGeneration;

    /// <summary>
    /// 取消尚未执行的旧搜索，避免连续输入时重复扫描和刷新列表。
    /// </summary>
    private CancellationTokenSource? _filterCancellationTokenSource;

    /// <summary>
    /// 页面是否已经完成首次索引。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 可用分类列表。
    /// </summary>
    public IReadOnlyList<string> Categories { get; } =
        ["全部", "内置任务", "地图追踪", "JS 脚本", "录制回放", "高级能力"];

    /// <summary>
    /// 当前索引中可用的一级目录范围；路线仓库通常用目录表达材料或目标分类。
    /// </summary>
    public ObservableCollection<string> Scopes { get; } = ["全部范围"];

    /// <summary>
    /// 可以作为添加目标的全部打开计划。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> TargetPlans
        => _taskPlanViewModel.Documents;

    /// <summary>
    /// 当前搜索文字。
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 当前分类。
    /// </summary>
    [ObservableProperty]
    private string _selectedCategory = "全部";

    /// <summary>
    /// 当前选择的目录或能力范围。
    /// </summary>
    [ObservableProperty]
    private string _selectedScope = "全部范围";

    /// <summary>
    /// 过滤后展示的任务库项目。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PuloniaTaskLibraryItemViewModel> _filteredItems = [];

    /// <summary>
    /// 当前选中的任务库项目。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskLibraryItemViewModel? _selectedItem;

    /// <summary>
    /// 当前选择的目标计划。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskPlanDocumentViewModel? _selectedTargetPlan;

    /// <summary>
    /// 当前项目按需加载的说明或 README 预览。
    /// </summary>
    [ObservableProperty]
    private string _previewText = "请选择一项任务或资源查看详情。";

    /// <summary>
    /// 页面是否正在建立或刷新索引。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 页面状态摘要。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 建立任务库页面视图模型。
    /// </summary>
    public PuloniaTaskLibraryViewModel(IPuloniaTaskService taskService,
        PuloniaTaskResourceCatalog resourceCatalog, PuloniaTaskPlanViewModel taskPlanViewModel,
        INavigationService navigationService)
    {
        _taskService = taskService;
        _resourceCatalog = resourceCatalog;
        _taskPlanViewModel = taskPlanViewModel;
        _navigationService = navigationService;
    }

    /// <summary>
    /// 页面首次导航时建立资源索引并准备目标计划列表。
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        await _taskPlanViewModel.OnNavigatedToAsync();
        SelectedTargetPlan ??= _taskPlanViewModel.SelectedDocument;
        if (!_isInitialized)
            await LoadLibraryAsync(forceRefresh: false);
    }

    /// <summary>
    /// 显式刷新本地能力资源索引。
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadLibraryAsync(forceRefresh: true);

    /// <summary>
    /// 把当前选择带入类型专用创建弹窗，确认后一次性加入目标计划。
    /// </summary>
    [RelayCommand]
    private void AddSelected()
    {
        if (SelectedItem is null || SelectedTargetPlan is null)
        {
            StatusMessage = "请先选择任务资源和目标计划。";
            return;
        }
        var creation = PuloniaTaskCreationDialog.Show(_taskService.Definitions, _resourceCatalog,
            SelectedItem.Definition.TaskType, SelectedItem.Resource?.RelativePath,
            Application.Current.MainWindow);
        if (creation is null)
            return;
        _taskPlanViewModel.InsertCreatedTask(SelectedTargetPlan, creation.Task);
        StatusMessage = $"已把“{creation.Task.Name}”添加到计划“{SelectedTargetPlan.Name}”。";
        _navigationService.Navigate(typeof(PuloniaTaskPlanPage));
    }

    /// <summary>
    /// 打开现有路线录制与编辑工具。
    /// </summary>
    [RelayCommand]
    private void OpenPathingTools() => _navigationService.Navigate(typeof(MapPathingPage));

    /// <summary>
    /// 打开现有 JS 安装、查看与开发工具。
    /// </summary>
    [RelayCommand]
    private void OpenJavaScriptTools() => _navigationService.Navigate(typeof(JsListPage));

    /// <summary>
    /// 打开现有键鼠录制与编辑工具。
    /// </summary>
    [RelayCommand]
    private void OpenKeyMouseTools() => _navigationService.Navigate(typeof(KeyMouseRecordPage));

    /// <summary>
    /// 打开现有资源仓库管理窗口，在任务库内完成来源更新、在线下载或离线导入。
    /// </summary>
    [RelayCommand]
    private void OpenResourceRepository() => ScriptRepoUpdater.Instance.OpenScriptRepoWindow();

    /// <summary>
    /// 搜索文字变化时只过滤缓存索引。
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _filterCancellationTokenSource?.Cancel();
        var cancellationTokenSource = new CancellationTokenSource();
        _filterCancellationTokenSource = cancellationTokenSource;
        _ = ApplyFilterDebouncedAsync(cancellationTokenSource);
    }

    /// <summary>
    /// 分类变化时只过滤缓存索引。
    /// </summary>
    partial void OnSelectedCategoryChanged(string value)
    {
        _filterCancellationTokenSource?.Cancel();
        ApplyFilter();
    }

    /// <summary>
    /// 目录范围变化时立即过滤已经建立的缓存索引。
    /// </summary>
    partial void OnSelectedScopeChanged(string value)
    {
        _filterCancellationTokenSource?.Cancel();
        ApplyFilter();
    }

    /// <summary>
    /// 选择变化后按需读取详情，未选中的资源不会解析。
    /// </summary>
    partial void OnSelectedItemChanged(PuloniaTaskLibraryItemViewModel? value)
    {
        var generation = ++_previewGeneration;
        if (value is null)
        {
            PreviewText = "请选择一项任务或资源查看详情。";
            return;
        }
        if (value.Resource is null)
        {
            PreviewText = $"{value.Definition.DisplayName}\n\n{value.Definition.Description}\n\n" +
                          $"任务类型：{value.Definition.TaskType}\n" +
                          $"需要游戏会话：{(value.Definition.RequiresGameSession ? "是" : "否")}";
            return;
        }
        PreviewText = "正在读取资源详情…";
        _ = LoadPreviewAsync(value, generation);
    }

    /// <summary>
    /// 建立全部能力及资源项目，索引截断会保留明确状态。
    /// </summary>
    private async Task LoadLibraryAsync(bool forceRefresh)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusMessage = forceRefresh ? "正在刷新任务库…" : "正在建立任务库索引…";
        try
        {
            _allItems.Clear();
            var truncatedTypeCount = 0;
            foreach (var definition in _taskService.Definitions)
            {
                var category = GetCategory(definition.TaskType);
                if (definition.TaskType is not ("pathing" or "javascript" or "keymouse"))
                {
                    _allItems.Add(new PuloniaTaskLibraryItemViewModel(definition, null, category));
                    continue;
                }
                var index = await _resourceCatalog.GetIndexAsync(definition, forceRefresh);
                if (index.IsTruncated)
                    truncatedTypeCount++;
                foreach (var resource in index.Resources)
                    _allItems.Add(new PuloniaTaskLibraryItemViewModel(definition, resource, category));
            }
            Scopes.Clear();
            Scopes.Add("全部范围");
            foreach (var scope in _allItems.Select(item => item.Scope).Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase))
                Scopes.Add(scope);
            if (!Scopes.Contains(SelectedScope))
                SelectedScope = "全部范围";
            ApplyFilter();
            _isInitialized = true;
            StatusMessage = $"任务库已索引 {_allItems.Count} 项能力与资源。";
            if (truncatedTypeCount > 0)
                StatusMessage += $" {truncatedTypeCount} 类资源达到安全扫描上限。";
        }
        catch (Exception ex)
        {
            StatusMessage = "任务库索引失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 按分类及名称、路径和任务类型过滤内存索引。
    /// </summary>
    private void ApplyFilter()
    {
        var keyword = SearchText.Trim();
        var matches = _allItems.Where(item =>
            (SelectedCategory == "全部" || item.Category == SelectedCategory)
            && (SelectedScope == "全部范围" || item.Scope == SelectedScope)
            && (string.IsNullOrWhiteSpace(keyword)
                || item.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        FilteredItems = new ObservableCollection<PuloniaTaskLibraryItemViewModel>(matches);
        SelectedItem = FilteredItems.FirstOrDefault();
    }

    /// <summary>
    /// 搜索输入短暂停顿后再过滤缓存，并丢弃已经被后续输入替代的查询。
    /// </summary>
    private async Task ApplyFilterDebouncedAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(180, cancellationTokenSource.Token);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // 连续输入会主动替换旧查询，这是预期控制流。
        }
        finally
        {
            if (ReferenceEquals(_filterCancellationTokenSource, cancellationTokenSource))
                _filterCancellationTokenSource = null;
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// 加载当前项目详情，并忽略已经过时的异步结果。
    /// </summary>
    private async Task LoadPreviewAsync(PuloniaTaskLibraryItemViewModel item, int generation)
    {
        try
        {
            var preview = await PuloniaTaskResourcePreviewLoader.LoadAsync(item.Resource!,
                item.Definition.TaskType);
            if (generation == _previewGeneration)
                PreviewText = preview.Text;
        }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
                PreviewText = "无法读取资源详情：" + ex.Message;
        }
    }

    /// <summary>
    /// 把稳定任务类型归入用户可见分类。
    /// </summary>
    private static string GetCategory(string taskType) => taskType switch
    {
        _ when taskType.StartsWith("builtin.", StringComparison.Ordinal) => "内置任务",
        "pathing" => "地图追踪",
        "javascript" => "JS 脚本",
        "keymouse" => "录制回放",
        _ => "高级能力"
    };
}
