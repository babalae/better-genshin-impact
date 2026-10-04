using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 任务创建弹窗视图模型，按能力定义展示资源选择与参数表单，并在确认后一次性生成节点数据。
/// </summary>
public partial class PuloniaTaskCreationDialogViewModel : ViewModel
{
    /// <summary>
    /// 本次创建入口请求的类型；builtin 表示从全部内置能力中选择。
    /// </summary>
    private readonly string _creationTaskType;

    /// <summary>
    /// 创建弹窗按当前任务类型使用的轻量资源索引。
    /// </summary>
    private readonly PuloniaTaskResourceCatalog _resourceCatalog;

    /// <summary>
    /// 尚未过滤的本地资源索引。
    /// </summary>
    private IReadOnlyList<PuloniaTaskResourceDescriptor> _allResources = [];

    /// <summary>
    /// 防止较早的异步预览覆盖用户后来选择的资源。
    /// </summary>
    private int _previewGeneration;

    /// <summary>
    /// 取消已经失去选择的预览，及时释放来源读取锁，避免连续切换时排队。
    /// </summary>
    private CancellationTokenSource? _previewCancellationTokenSource;

    /// <summary>
    /// 取消尚未执行的旧资源搜索，避免大索引在连续输入时反复刷新。
    /// </summary>
    private CancellationTokenSource? _filterCancellationTokenSource;

    /// <summary>
    /// 设置推荐名称时阻止其被误判为用户自定义。
    /// </summary>
    private bool _isSettingSuggestedName;

    /// <summary>
    /// 用户是否已经主动修改任务名称。
    /// </summary>
    private bool _isTaskNameCustomized;

    /// <summary>
    /// 当前 JS 项目声明的设置文件是否解析失败；失败时不能静默创建缺少设置的任务。
    /// </summary>
    private bool _javaScriptSettingsLoadFailed;

    /// <summary>
    /// 用户可选择的能力定义；非内置入口通常只有一项。
    /// </summary>
    public IReadOnlyList<PuloniaTaskDefinition> AvailableDefinitions { get; }

    /// <summary>
    /// 目录资源支持的引用与展开方式。
    /// </summary>
    public IReadOnlyList<PuloniaDirectoryImportModeOption> DirectoryImportModes { get; } =
    [
        new("reference", "引用目录（推荐）", "保存固定目录版本，资源清单在运行准备时展开；计划树保持简洁。"),
        new("structured", "展开为任务并保留目录", "把当前文件清单写成可逐项编辑的任务树，并保留子目录结构。"),
        new("flat", "展开为扁平任务", "把当前文件清单写入一个分组，不保留子目录层级。")
    ];

    /// <summary>
    /// 当前能力的参数编辑字段。
    /// </summary>
    public ObservableCollection<PuloniaTaskParameterFieldViewModel> ParameterFields { get; } = [];

    /// <summary>
    /// 当前 JS 项目从 manifest.settings_ui 解析出的自定义设置字段。
    /// </summary>
    public ObservableCollection<PuloniaJsScriptSettingFieldViewModel> JavaScriptSettingFields { get; } = [];

    /// <summary>
    /// 用户确认后的创建结果；取消或校验失败时为空。
    /// </summary>
    public PuloniaTaskCreationResult? Result { get; private set; }

    /// <summary>
    /// 请求宿主窗口按确认或取消结果关闭。
    /// </summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 弹窗标题。
    /// </summary>
    public string WindowTitle => _creationTaskType switch
    {
        "builtin" => "添加内置任务",
        "pathing" => "添加地图追踪任务",
        "javascript" => "添加 JS 脚本任务",
        "keymouse" => "添加录制回放任务",
        "shell" => "添加 Shell 任务",
        "csharp" => "添加进程内 C# 任务",
        _ => "添加任务"
    };

    /// <summary>
    /// 当前入口是否允许选择多个内置能力。
    /// </summary>
    public bool IsBuiltinPicker => _creationTaskType == "builtin";

    /// <summary>
    /// 当前能力是否必须选择本地资源。
    /// </summary>
    public bool RequiresResource => SelectedDefinition?.TaskType is "pathing" or "javascript" or "keymouse";

    /// <summary>
    /// 当前能力是否声明了可编辑参数。
    /// </summary>
    public bool HasParameters => ParameterFields.Count > 0 || JavaScriptSettingFields.Count > 0;

    /// <summary>
    /// 当前能力是否使用地图追踪树形资源选择器。
    /// </summary>
    public bool IsPathingResourceSelector => SelectedDefinition?.TaskType == "pathing";

    /// <summary>
    /// 当前能力是否为 JS 脚本。
    /// </summary>
    public bool IsJavaScriptTask => SelectedDefinition?.TaskType == "javascript";

    /// <summary>
    /// 当前 JS 项目是否声明了可展示的自定义设置项。
    /// </summary>
    public bool HasJavaScriptSettings => JavaScriptSettingFields.Count > 0;

    /// <summary>
    /// 当前资源详情区标题；JS 详情只展示 README.md。
    /// </summary>
    public string ResourcePreviewTitle => IsJavaScriptTask ? "README.md" : "资源详情";

    /// <summary>
    /// 当前资源是否可以使用原生 Markdown 控件展示 README.md。
    /// </summary>
    public bool ShowMarkdownPreview => IsJavaScriptTask && !string.IsNullOrWhiteSpace(ReadmeFilePath);

    /// <summary>
    /// 当前资源是否使用普通文本详情或 README 空状态。
    /// </summary>
    public bool ShowTextResourcePreview => !ShowMarkdownPreview;

    /// <summary>
    /// 资源索引或当前选中资源详情是否仍在加载。
    /// </summary>
    public bool IsLoading => IsBusy || IsResourceDetailsLoading;

    /// <summary>
    /// 当前能力说明。
    /// </summary>
    public string DefinitionDescription => SelectedDefinition?.Description ?? string.Empty;

    /// <summary>
    /// 资源搜索框提示文本。
    /// </summary>
    public string ResourceSearchPlaceholder => SelectedDefinition?.TaskType switch
    {
        "pathing" => "搜索路线名称或相对路径",
        "javascript" => "搜索脚本名称或目录",
        "keymouse" => "搜索录制名称或相对路径",
        _ => "搜索资源"
    };

    /// <summary>
    /// 当前选择是否是支持批量导入的路线或录制目录。
    /// </summary>
    public bool IsDirectoryImport => SelectedResource?.IsDirectory == true
                                     && SelectedDefinition?.TaskType is "pathing" or "keymouse";

    /// <summary>
    /// 当前目录添加方式的说明。
    /// </summary>
    public string DirectoryImportModeDescription => SelectedDirectoryImportMode?.Description ?? string.Empty;

    /// <summary>
    /// 当前选中的能力定义。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskDefinition? _selectedDefinition;

    /// <summary>
    /// 用户确认的任务名称。
    /// </summary>
    [ObservableProperty]
    private string _taskName = string.Empty;

    /// <summary>
    /// 资源搜索关键字。
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 过滤后展示的资源列表。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PuloniaTaskResourceDescriptor> _filteredResources = [];

    /// <summary>
    /// 过滤后展示的地图追踪目录树。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PuloniaTaskResourceTreeNodeViewModel> _filteredResourceTree = [];

    /// <summary>
    /// 地图追踪目录树中当前选中的节点。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskResourceTreeNodeViewModel? _selectedResourceTreeNode;

    /// <summary>
    /// 当前选中的资源。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskResourceDescriptor? _selectedResource;

    /// <summary>
    /// 当前资源的按需预览。
    /// </summary>
    [ObservableProperty]
    private string _resourcePreview = "请选择一项资源查看详情。";

    /// <summary>
    /// 当前 JS 项目的 README.md 完整路径。
    /// </summary>
    [ObservableProperty]
    private string? _readmeFilePath;

    /// <summary>
    /// 当前 JS 项目自定义设置的加载状态或空状态说明。
    /// </summary>
    [ObservableProperty]
    private string _javaScriptSettingsMessage = "请选择一个 JS 项目。";

    /// <summary>
    /// 弹窗底部的加载或校验状态。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 是否正在后台建立轻量资源索引。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 是否正在读取当前资源的 README、清单和脚本设置定义。
    /// </summary>
    [ObservableProperty]
    private bool _isResourceDetailsLoading;

    /// <summary>
    /// 当前选择的目录添加方式。
    /// </summary>
    [ObservableProperty]
    private PuloniaDirectoryImportModeOption? _selectedDirectoryImportMode;

    /// <summary>
    /// 批量添加目录时是否包含全部子目录。
    /// </summary>
    [ObservableProperty]
    private bool _includeSubdirectories = true;

    /// <summary>
    /// 使用当前已注册能力建立任务创建视图模型。
    /// </summary>
    public PuloniaTaskCreationDialogViewModel(IReadOnlyList<PuloniaTaskDefinition> definitions,
        PuloniaTaskResourceCatalog resourceCatalog, string creationTaskType)
    {
        _creationTaskType = creationTaskType;
        _resourceCatalog = resourceCatalog;
        AvailableDefinitions = definitions
            .Where(definition => creationTaskType == "builtin"
                ? definition.TaskType.StartsWith("builtin.", StringComparison.Ordinal)
                : definition.TaskType == creationTaskType)
            .ToList();
        if (AvailableDefinitions.Count == 0)
            throw new InvalidOperationException($"没有注册 Pulonia 任务类型 {creationTaskType}。");

        SelectedDirectoryImportMode = DirectoryImportModes[0];
        SelectedDefinition = AvailableDefinitions[0];
    }

    /// <summary>
    /// 弹窗显示后在后台建立当前类型的轻量资源索引。
    /// </summary>
    public async Task InitializeAsync(bool forceRefresh = false)
    {
        if (!RequiresResource || SelectedDefinition is null || IsBusy || _isClosed)
            return;
        var definition = SelectedDefinition;
        var cancellationTokenSource = new CancellationTokenSource();
        _resourceLoadCancellationTokenSource = cancellationTokenSource;
        var ct = cancellationTokenSource.Token;
        IsBusy = true;
        StatusMessage = "正在读取已拉取的资源清单…";
        try
        {
            if (SupportsRepositories)
            {
                await ReloadRepositoriesAsync(ct);
                if (SelectedRepository is null)
                {
                    ClearResourceSelection();
                    StatusMessage = "尚未添加仓库，请添加远程仓库或已拉取的本地仓库。";
                    return;
                }
                await _resourceCatalog.Repositories.SelectRepositoryAsync(definition.TaskType, SelectedRepository.Id, ct);
            }
            var repositoryId = SupportsRepositories ? SelectedRepository?.Id : null;
            var index = await _resourceCatalog.GetIndexAsync(definition, forceRefresh, ct, repositoryId);
            ct.ThrowIfCancellationRequested();
            _allResources = index.Resources;
            ApplyResourceFilter();
            StatusMessage = index.IsTruncated
                ? $"资源超过安全扫描上限，仅显示已建立索引的 {_allResources.Count} 项。"
                : $"已找到 {_allResources.Count} 项可用资源。";
            if (index.SkippedDirectoryCount > 0)
                StatusMessage += $" 已跳过 {index.SkippedDirectoryCount} 个不可访问目录。";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 关闭弹窗后取消索引请求，不回写已经关闭页面的资源状态。
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                   or Newtonsoft.Json.JsonException or LibGit2Sharp.LibGit2SharpException)
        {
            _allResources = [];
            ApplyResourceFilter();
            StatusMessage = "读取资源失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_resourceLoadCancellationTokenSource, cancellationTokenSource))
                _resourceLoadCancellationTokenSource = null;
            cancellationTokenSource.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 丢弃当前类型的界面筛选结果并显式刷新共享资源索引。
    /// </summary>
    [RelayCommand]
    private async Task RefreshResourcesAsync()
    {
        await InitializeAsync(forceRefresh: true);
    }

    /// <summary>
    /// 打开现有脚本仓库获取更多资源，并在仓库关闭后刷新当前类型清单。
    /// </summary>
    [RelayCommand]
    private async Task OpenResourceRepositoryAsync()
    {
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
        await InitializeAsync(forceRefresh: true);
    }

    /// <summary>
    /// 能力选择变化后重建 Schema 表单并更新推荐名称。
    /// </summary>
    partial void OnSelectedDefinitionChanged(PuloniaTaskDefinition? value)
    {
        ParameterFields.Clear();
        JavaScriptSettingFields.Clear();
        ReadmeFilePath = null;
        if (value is not null)
        {
            var properties = value.ParameterSchema["properties"] as JObject ?? new JObject();
            var required = new HashSet<string>(
                (value.ParameterSchema["required"] as JArray ?? new JArray()).Values<string>()
                .Where(name => name is not null).Cast<string>(), StringComparer.Ordinal);
            foreach (var property in properties.Properties())
            {
                if (property.Value is not JObject fieldSchema)
                    continue;
                // JS 的 settings 对象由所选项目自己的 settings_ui 表单生成，不显示通用 JSON 编辑框。
                if (value.TaskType == "javascript" && property.Name == "settings")
                    continue;
                // 创建时保留常用选项；完整行走、战斗、食物和 Shell 设置通过节点配置的专用表单编辑。
                if (PuloniaTaskCommonSettings.IsCommonParameter(value.TaskType, property.Name)
                    && property.Name is not ("party_name" or "skip_party_switch" or "auto_pick_enabled"
                        or "auto_fight_enabled" or "auto_skip_enabled"))
                    continue;
                var defaultProperty = value.DefaultParameters.Property(property.Name);
                ParameterFields.Add(new PuloniaTaskParameterFieldViewModel(property.Name, fieldSchema,
                    defaultProperty?.Value, defaultProperty is not null, required.Contains(property.Name)));
            }
            SetSuggestedName(value.DisplayName);
        }
        OnPropertyChanged(nameof(RequiresResource));
        OnPropertyChanged(nameof(SupportsRepositories));
        NotifyRepositoryCommands();
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(IsPathingResourceSelector));
        OnPropertyChanged(nameof(IsJavaScriptTask));
        OnPropertyChanged(nameof(HasJavaScriptSettings));
        OnPropertyChanged(nameof(ResourcePreviewTitle));
        OnPropertyChanged(nameof(ShowMarkdownPreview));
        OnPropertyChanged(nameof(ShowTextResourcePreview));
        OnPropertyChanged(nameof(DefinitionDescription));
        OnPropertyChanged(nameof(ResourceSearchPlaceholder));
    }

    /// <summary>
    /// 记录用户对推荐名称的主动修改。
    /// </summary>
    partial void OnTaskNameChanged(string value)
    {
        if (!_isSettingSuggestedName)
            _isTaskNameCustomized = true;
    }

    /// <summary>
    /// 搜索文字变化后仅过滤缓存索引，不重新扫描磁盘。
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _filterCancellationTokenSource?.Cancel();
        var cancellationTokenSource = new CancellationTokenSource();
        _filterCancellationTokenSource = cancellationTokenSource;
        _ = ApplyResourceFilterDebouncedAsync(cancellationTokenSource);
    }

    /// <summary>
    /// 资源选择变化后异步加载该项详情，并在名称未自定义时使用资源名称。
    /// </summary>
    partial void OnSelectedResourceChanged(PuloniaTaskResourceDescriptor? value)
    {
        OnPropertyChanged(nameof(IsDirectoryImport));
        var generation = ++_previewGeneration;
        _previewCancellationTokenSource?.Cancel();
        ReadmeFilePath = null;
        JavaScriptSettingFields.Clear();
        _javaScriptSettingsLoadFailed = false;
        JavaScriptSettingsMessage = IsJavaScriptTask ? "正在读取脚本设置…" : string.Empty;
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(HasJavaScriptSettings));
        if (value is null)
        {
            IsResourceDetailsLoading = false;
            ResourcePreview = _allResources.Count == 0 ? "没有可预览的资源。" : "请选择一项资源查看详情。";
            if (IsJavaScriptTask)
                JavaScriptSettingsMessage = "请选择一个 JS 项目。";
            return;
        }

        SetSuggestedName(value.DisplayName);
        var cancellationTokenSource = new CancellationTokenSource();
        _previewCancellationTokenSource = cancellationTokenSource;
        _ = LoadResourcePreviewAsync(value, generation, cancellationTokenSource);
    }

    /// <summary>
    /// 树中选择变化后同步当前资源，后续预览和创建逻辑仍只处理统一资源描述。
    /// </summary>
    partial void OnSelectedResourceTreeNodeChanged(PuloniaTaskResourceTreeNodeViewModel? value)
    {
        if (IsPathingResourceSelector)
            SelectedResource = value?.Resource;
    }

    /// <summary>
    /// 资源索引加载状态变化后刷新统一加载状态。
    /// </summary>
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoading));
        NotifyRepositoryCommands();
    }

    /// <summary>
    /// 资源详情加载状态变化后刷新统一加载状态。
    /// </summary>
    partial void OnIsResourceDetailsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsLoading));

    /// <summary>
    /// README 文件变化后切换原生 Markdown 预览和普通文本空状态。
    /// </summary>
    partial void OnReadmeFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowMarkdownPreview));
        OnPropertyChanged(nameof(ShowTextResourcePreview));
    }

    /// <summary>
    /// 目录添加方式变化后更新说明文本。
    /// </summary>
    partial void OnSelectedDirectoryImportModeChanged(PuloniaDirectoryImportModeOption? value)
        => OnPropertyChanged(nameof(DirectoryImportModeDescription));

    /// <summary>
    /// 确认名称、资源和参数均有效后生成一次性结果并关闭弹窗。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        Result = null;
        if (IsLoading)
        {
            StatusMessage = "资源仍在加载，请稍候。";
            return;
        }
        if (SelectedDefinition is null)
        {
            StatusMessage = "请选择任务类型。";
            return;
        }
        var name = TaskName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "任务名称不能为空。";
            return;
        }
        if (RequiresResource && SelectedResource is null)
        {
            StatusMessage = "请先选择要执行的资源。";
            return;
        }
        if (SelectedDefinition.TaskType == "javascript" && _javaScriptSettingsLoadFailed)
        {
            StatusMessage = "脚本设置文件解析失败，请修复脚本配置或选择其他脚本。";
            return;
        }
        // 仓库资源尚未提取到本地目录，由固定版本读取和提取校验确认存在性；传统资源仍检查磁盘。
        if (SelectedResource is { Resource: null } resource
            && resource.IsDirectory != Directory.Exists(resource.FullPath))
        {
            StatusMessage = "选中的资源已经不存在，请刷新后重新选择。";
            return;
        }
        if (SelectedResource is { Resource: null, IsDirectory: false } fileResource && !File.Exists(fileResource.FullPath))
        {
            StatusMessage = "选中的资源已经不存在，请刷新后重新选择。";
            return;
        }

        try
        {
            // 节点只保存相对能力默认值发生变化的字段，来源预览仍能区分默认值与显式设置。
            var overrides = new JObject();
            foreach (var field in ParameterFields)
            {
                var value = field.BuildValue();
                if (field.ShouldPersist(value))
                    overrides[field.Name] = value;
            }
            if (SelectedDefinition.TaskType == "javascript")
            {
                var scriptSettings = BuildJavaScriptSettings();
                if (scriptSettings.Count > 0)
                    overrides["settings"] = scriptSettings;
            }

            var effectiveParameters = (JObject)SelectedDefinition.DefaultParameters.DeepClone();
            foreach (var property in overrides.Properties())
                effectiveParameters[property.Name] = property.Value.DeepClone();
            PuloniaTaskValidator.ValidateParameters(effectiveParameters, SelectedDefinition.ParameterSchema,
                "task/parameters");
            ValidateKnownRequiredValues(SelectedDefinition.TaskType, effectiveParameters);

            IsBusy = true;
            StatusMessage = RequiresResource ? "正在固定资源版本…" : "正在创建任务…";
            var task = await BuildTaskAsync(name, SelectedDefinition, SelectedResource, overrides);
            if (_isClosed) return;
            Result = new PuloniaTaskCreationResult(task);
            StatusMessage = string.Empty;
            IsBusy = false;
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is FormatException or PuloniaTaskValidationException or IOException
                                   or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                   or LibGit2Sharp.LibGit2SharpException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        {
            StatusMessage = ex.Message;
            IsBusy = false;
        }
    }

    /// <summary>
    /// 取消创建，不产生任何任务树变更。
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 关闭弹窗后不再继续读取、刷新预览或执行尚未完成的搜索。
        Close();
        RequestClose?.Invoke(this, false);
    }

    /// <summary>
    /// 使用推荐名称，但不覆盖用户已经输入的自定义名称。
    /// </summary>
    private void SetSuggestedName(string suggestedName)
    {
        if (_isTaskNameCustomized || string.IsNullOrWhiteSpace(suggestedName))
            return;
        _isSettingSuggestedName = true;
        try
        {
            TaskName = suggestedName;
        }
        finally
        {
            _isSettingSuggestedName = false;
        }
    }

    /// <summary>
    /// 使用缓存索引完成不区分大小写的名称和路径过滤。
    /// </summary>
    private void ApplyResourceFilter()
    {
        var keyword = SearchText.Trim();
        if (IsPathingResourceSelector)
        {
            var selectedRelativePath = SelectedResource?.RelativePath;
            FilteredResources = [];
            FilteredResourceTree = BuildPathingResourceTree(keyword);
            var selectedNode = FindTreeNode(FilteredResourceTree, selectedRelativePath);
            SelectedResourceTreeNode = selectedNode;
            if (selectedNode is null)
                SelectedResource = null;
            return;
        }

        SelectedResourceTreeNode = null;
        FilteredResourceTree = [];
        var matches = string.IsNullOrWhiteSpace(keyword)
            ? _allResources
            : _allResources.Where(resource => resource.SearchText.Contains(keyword,
                StringComparison.OrdinalIgnoreCase)).ToList();
        FilteredResources = new ObservableCollection<PuloniaTaskResourceDescriptor>(matches);
        SelectedResource = FilteredResources.FirstOrDefault();
    }

    /// <summary>
    /// 资源搜索输入短暂停顿后再过滤缓存，并丢弃已经过时的查询。
    /// </summary>
    private async Task ApplyResourceFilterDebouncedAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(180, cancellationTokenSource.Token);
            ApplyResourceFilter();
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
    /// 加载当前选择的轻量详情；JS 只读取清单、设置定义和 README 路径。
    /// </summary>
    private async Task LoadResourcePreviewAsync(PuloniaTaskResourceDescriptor resource, int generation,
        CancellationTokenSource cancellationTokenSource)
    {
        IsResourceDetailsLoading = true;
        ResourcePreview = "正在读取资源详情…";
        var taskType = SelectedDefinition?.TaskType;
        try
        {
            var preview = await PuloniaTaskResourcePreviewLoader.LoadAsync(resource, taskType,
                cancellationTokenSource.Token, _resourceCatalog.Repositories);
            if (generation != _previewGeneration)
                return;
            ResourcePreview = preview.Text;
            ReadmeFilePath = preview.MarkdownFilePath;
            foreach (var item in preview.JavaScriptSettingItems)
                JavaScriptSettingFields.Add(new PuloniaJsScriptSettingFieldViewModel(item));
            JavaScriptSettingsMessage = preview.JavaScriptSettingsError
                                        ?? (JavaScriptSettingFields.Count == 0
                                            ? "此脚本未提供自定义设置。"
                                            : string.Empty);
            _javaScriptSettingsLoadFailed = !string.IsNullOrWhiteSpace(preview.JavaScriptSettingsError);
            OnPropertyChanged(nameof(HasParameters));
            OnPropertyChanged(nameof(HasJavaScriptSettings));
            if (!string.IsNullOrWhiteSpace(preview.SuggestedName))
                SetSuggestedName(preview.SuggestedName);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            // 切换选择或关闭弹窗属于预期取消，不显示资源读取失败。
        }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
            {
                ResourcePreview = "无法读取资源详情：" + ex.Message;
                JavaScriptSettingsMessage = IsJavaScriptTask ? "无法读取脚本设置。" : string.Empty;
                _javaScriptSettingsLoadFailed = IsJavaScriptTask;
            }
        }
        finally
        {
            if (generation == _previewGeneration)
                IsResourceDetailsLoading = false;
            if (ReferenceEquals(_previewCancellationTokenSource, cancellationTokenSource))
                _previewCancellationTokenSource = null;
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// 将脚本专用表单汇总为执行器读取的 settings JSON 对象。
    /// </summary>
    private JObject BuildJavaScriptSettings()
    {
        var settings = new JObject();
        foreach (var field in JavaScriptSettingFields)
        {
            if (!field.IsSupported)
                throw new FormatException($"脚本设置“{field.Label}”使用了不支持的类型：{field.Type}。");
            var value = field.BuildValue();
            if (value is not null)
                settings[field.Name] = value;
        }
        return settings;
    }

    /// <summary>
    /// 从轻量资源索引建立地图追踪目录树，并在搜索时保留命中项的祖先链。
    /// </summary>
    private ObservableCollection<PuloniaTaskResourceTreeNodeViewModel> BuildPathingResourceTree(string keyword)
    {
        var childrenByParent = new Dictionary<string, List<PuloniaTaskResourceDescriptor>>(
            StringComparer.OrdinalIgnoreCase);
        var roots = new List<PuloniaTaskResourceDescriptor>();
        foreach (var resource in _allResources)
        {
            if (resource.IsRootDirectory)
            {
                roots.Add(resource);
                continue;
            }

            var normalizedPath = NormalizeRelativePath(resource.RelativePath);
            var separatorIndex = normalizedPath.LastIndexOf('/');
            var parentPath = separatorIndex < 0 ? "." : normalizedPath[..separatorIndex];
            if (!childrenByParent.TryGetValue(parentPath, out var children))
            {
                children = [];
                childrenByParent[parentPath] = children;
            }
            children.Add(resource);
        }

        var result = new ObservableCollection<PuloniaTaskResourceTreeNodeViewModel>();
        foreach (var root in roots)
        {
            var node = BuildFilteredTreeNode(root, childrenByParent, keyword, includeCompleteSubtree: false);
            if (node is not null)
                result.Add(node);
        }
        return result;
    }

    /// <summary>
    /// 递归建立一个筛选后的树节点；目录自身命中时展示其完整子树。
    /// </summary>
    private static PuloniaTaskResourceTreeNodeViewModel? BuildFilteredTreeNode(
        PuloniaTaskResourceDescriptor resource,
        IReadOnlyDictionary<string, List<PuloniaTaskResourceDescriptor>> childrenByParent,
        string keyword, bool includeCompleteSubtree)
    {
        var currentMatches = string.IsNullOrWhiteSpace(keyword)
                             || resource.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        var includeAllChildren = includeCompleteSubtree || currentMatches;
        var childNodes = new List<PuloniaTaskResourceTreeNodeViewModel>();
        var relativePath = NormalizeRelativePath(resource.RelativePath);
        if (childrenByParent.TryGetValue(relativePath, out var children))
        {
            foreach (var child in children.OrderByDescending(item => item.IsDirectory)
                         .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var childNode = BuildFilteredTreeNode(child, childrenByParent, keyword, includeAllChildren);
                if (childNode is not null)
                    childNodes.Add(childNode);
            }
        }

        if (!includeCompleteSubtree && !currentMatches && childNodes.Count == 0)
            return null;
        var node = new PuloniaTaskResourceTreeNodeViewModel(resource,
            resource.IsRootDirectory || !string.IsNullOrWhiteSpace(keyword));
        foreach (var childNode in childNodes)
            node.Children.Add(childNode);
        return node;
    }

    /// <summary>
    /// 在当前筛选树中按相对路径恢复选择。
    /// </summary>
    private static PuloniaTaskResourceTreeNodeViewModel? FindTreeNode(
        IEnumerable<PuloniaTaskResourceTreeNodeViewModel> nodes, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                return node;
            var child = FindTreeNode(node.Children, relativePath);
            if (child is not null)
                return child;
        }
        return null;
    }

    /// <summary>
    /// 将资源相对路径统一为树索引使用的正斜杠形式。
    /// </summary>
    private static string NormalizeRelativePath(string path)
    {
        if (path == ".")
            return path;
        return path.Replace('\\', '/').Trim('/');
    }

    /// <summary>
    /// 补充当前 Schema 子集尚不能表达的非空业务约束。
    /// </summary>
    private static void ValidateKnownRequiredValues(string taskType, JObject parameters)
    {
        if (taskType == "shell" && string.IsNullOrWhiteSpace(parameters.Value<string>("file_name")))
            throw new FormatException("“程序或命令”不能为空。");
        if (taskType == "csharp" && string.IsNullOrWhiteSpace(parameters.Value<string>("operation")))
            throw new FormatException("“C# 操作名”不能为空。");
    }

    /// <summary>
    /// 按单资源、目录引用或目录展开方式建立最终任务节点并固定资源版本。
    /// </summary>
    private async Task<PuloniaTask> BuildTaskAsync(string name, PuloniaTaskDefinition definition,
        PuloniaTaskResourceDescriptor? resource, JObject overrides)
    {
        if (resource is null)
        {
            return new PuloniaTask
            {
                Name = name,
                TaskType = definition.TaskType,
                Parameters = overrides
            };
        }

        // 提取选中版本成功后才建立节点，计划不会保存未完成缓存的资源引用。
        if (resource.Resource is { } repositoryResource)
            resource = resource.WithFullPath(await _resourceCatalog.Repositories.MaterializeAsync(repositoryResource, definition.TaskType));

        if (!IsDirectoryImport)
        {
            var version = resource.IsDirectory
                ? await ComputeDirectoryVersionAsync(resource.FullPath)
                : await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(resource.FullPath);
            return new PuloniaTask
            {
                Name = name,
                TaskType = definition.TaskType,
                Path = resource.Resource is null ? resource.RelativePath : null,
                Resource = resource.Resource,
                ResourceVersion = version,
                Parameters = overrides
            };
        }

        var mode = SelectedDirectoryImportMode?.Key ?? "reference";
        var files = await _resourceCatalog.GetDirectoryFilesAsync(resource.FullPath, "*.json",
            IncludeSubdirectories);
        if (files.Count == 0)
            throw new FormatException("选中的目录中没有可添加的 JSON 资源。");

        if (mode == "reference")
        {
            var version = await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(resource.FullPath, files);
            var reference = new PuloniaTask
            {
                Name = name,
                TaskType = "group",
                Source = new PuloniaTaskSource
                {
                    Kind = "directory",
                    Path = resource.Resource is null ? resource.FullPath : null,
                    Resource = resource.Resource,
                    TaskType = definition.TaskType,
                    Recursive = IncludeSubdirectories,
                    Version = version
                }
            };
            AddGroupParameterOverride(reference, definition, overrides);
            return reference;
        }

        var group = new PuloniaTask { Name = name, TaskType = "group" };
        AddGroupParameterOverride(group, definition, overrides);
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            StatusMessage = $"正在固定资源版本（{index + 1}/{files.Count}）…";
            var leaf = new PuloniaTask
            {
                Name = Path.GetFileNameWithoutExtension(file),
                TaskType = definition.TaskType,
                Path = resource.Resource is null ? Path.GetRelativePath(definition.ResourceBaseDirectory!, file) : null,
                Resource = resource.Resource?.WithPath(resource.Resource.RelativePath + "/" + Path.GetRelativePath(resource.FullPath, file).Replace('\\', '/')),
                ResourceVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(file)
            };
            if (mode == "flat")
            {
                group.Children.Add(leaf);
                continue;
            }
            AddStructuredLeaf(group, resource.FullPath, file, leaf);
        }
        return group;
    }

    /// <summary>
    /// 只计算 JS 根目录 manifest.json 和 main.js，保持与运行准备和资源更新检查一致的范围。
    /// </summary>
    private Task<string> ComputeDirectoryVersionAsync(string directory)
    {
        return PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(directory);
    }

    /// <summary>
    /// 把创建表单中的显式设置作为分组公共参数应用到批量资源。
    /// </summary>
    private static void AddGroupParameterOverride(PuloniaTask group, PuloniaTaskDefinition definition,
        JObject overrides)
    {
        if (overrides.Count == 0)
            return;
        group.ParameterOverrides.Add(new PuloniaTaskParameterOverride
        {
            TaskType = definition.TaskType,
            SchemaVersion = definition.SchemaVersion,
            Values = (JObject)overrides.DeepClone()
        });
    }

    /// <summary>
    /// 按选中目录的相对层级建立分组，并把叶子任务放入对应目录分组。
    /// </summary>
    private static void AddStructuredLeaf(PuloniaTask rootGroup, string selectedDirectory, string file,
        PuloniaTask leaf)
    {
        var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(selectedDirectory, file));
        var parent = rootGroup;
        if (!string.IsNullOrWhiteSpace(relativeDirectory) && relativeDirectory != ".")
        {
            foreach (var segment in relativeDirectory.Split(Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var childGroup = parent.Children.FirstOrDefault(child => child.TaskType == "group"
                                                                         && child.Name == segment);
                if (childGroup is null)
                {
                    childGroup = new PuloniaTask { Name = segment, TaskType = "group" };
                    parent.Children.Add(childGroup);
                }
                parent = childGroup;
            }
        }
        parent.Children.Add(leaf);
    }

}
