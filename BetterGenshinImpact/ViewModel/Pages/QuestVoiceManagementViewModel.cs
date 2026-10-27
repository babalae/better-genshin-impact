using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>管理外置 routing JSON 的独立草稿，不直接改动生产会话或统一配置。</summary>
public partial class QuestVoiceManagementViewModel : ObservableObject
{
    /// <summary>页面共享状态和配置的所有者。</summary>
    private readonly QuestVoicePageViewModel _owner;
    /// <summary>解析当前配置文件路径和 Provider 设置。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>生产试听及取消入口。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>负责严格校验、乐观锁和备份的存储边界。</summary>
    private readonly RoutingCatalogStore _store;
    /// <summary>真正加载的目标，防止修改路径后将旧草稿误写新文件。</summary>
    private string? _loadedPath;
    /// <summary>加载时摘要，保存时拒绝覆盖外部更新。</summary>
    private string? _expectedSha256;
    /// <summary>填充草稿时抑制脏状态和旅行者自动补齐。</summary>
    private bool _loading;
    /// <summary>区分声音刷新代次，实例切换后旧结果不能回写。</summary>
    private int _presetRequestGeneration;
    /// <summary>跟踪独立引擎实例地址变更。</summary>
    private readonly HashSet<QuestVoiceEngineConfig> _observedPresetConfigs = new();
    /// <summary>是否已经加载或明确新建了目录。</summary>
    [ObservableProperty] private bool _isLoaded;
    /// <summary>编辑器自身文件操作期间也禁止写入。</summary>
    [ObservableProperty] private bool _isWorking;
    /// <summary>未保存的任何输入都会明确标记。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DraftNotice))]
    private bool _hasChanges;
    /// <summary>编辑器最近操作的可读结果。</summary>
    [ObservableProperty] private string _editStatus = "正在准备角色目录；有效修改自动保存，下次启动配音生效。";
    /// <summary>角色列表过滤文本，不修改目录。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredSpeakers))]
    private string _speakerSearch = "";
    /// <summary>选中角色只决定显示，输入存放在各自对象里。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSpeakerVoiceId))]
    private QuestVoiceManagementSpeaker? _selectedSpeaker;
    /// <summary>选中独立声线。</summary>
    [ObservableProperty] private QuestVoiceManagementProfile? _selectedProfile;
    /// <summary>玩家多个昵称，一行一项。</summary>
    [ObservableProperty] private string _playerNames = "";
    /// <summary>旅行者角色引用，空表示未指定。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTravelerId))]
    private string _travelerId = "";
    /// <summary>普通 NPC 兜底声线引用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFallbackVoiceId))]
    private string _fallbackVoiceId = "";
    /// <summary>旁白声线引用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNarratorVoiceId))]
    private string _narratorVoiceId = "";
    /// <summary>角色没有有效声线时的处理策略。</summary>
    [ObservableProperty] private MissingVoicePolicy _missingVoicePolicy;
    /// <summary>独立声线试听文本。</summary>
    [ObservableProperty] private string _previewText = "你好，旅行者。欢迎来到提瓦特。";
    /// <summary>预设下拉只传递明确非空选择，列表重置不会清空 profile 的音色。</summary>
    public TtsVoiceInfo? SelectedPresetVoice
    {
        get => PresetVoices.FirstOrDefault(x => x.Id == SelectedProfile?.ProviderVoiceId);
        set
        {
            if (CanEditDraft && value is not null && SelectedProfile is not null)
                SelectedProfile.ProviderVoiceId = value.Id;
        }
    }
    /// <summary>忽略选择列表更新引发的暂时空值；用户通过空字符串条目明确清空。</summary>
    public string? SelectedTravelerId { get => TravelerId; set { if (CanEditDraft && value is not null) TravelerId = value; } }
    /// <summary>兜底列表的暂时空选择不能覆盖已有草稿。</summary>
    public string? SelectedFallbackVoiceId { get => FallbackVoiceId; set { if (CanEditDraft && value is not null) FallbackVoiceId = value; } }
    /// <summary>旁白列表的暂时空选择不能覆盖已有草稿。</summary>
    public string? SelectedNarratorVoiceId { get => NarratorVoiceId; set { if (CanEditDraft && value is not null) NarratorVoiceId = value; } }
    /// <summary>角色引用使用显式空条目清除，切换或刷新列表均保留原输入。</summary>
    public string? SelectedSpeakerVoiceId
    {
        get => SelectedSpeaker?.VoiceId;
        set { if (CanEditDraft && SelectedSpeaker is not null && value is not null) { SelectedSpeaker.VoiceId = value; OnPropertyChanged(); } }
    }
    /// <summary>当前 profile 的 Provider 预设音色。</summary>
    public ObservableCollection<TtsVoiceInfo> PresetVoices { get; } = new();
    /// <summary>全部角色草稿，不随搜索过滤丢失。</summary>
    public ObservableCollection<QuestVoiceManagementSpeaker> Speakers { get; } = new();
    /// <summary>独立声线草稿，可被多个角色共享。</summary>
    public ObservableCollection<QuestVoiceManagementProfile> Profiles { get; } = new();
    /// <summary>只读注册表决定可选择的 Provider。</summary>
    public IReadOnlyList<TtsProviderInfo> Providers => TtsProviderRegistry.Providers;
    /// <summary>与生产实例身份保持一致。</summary>
    public IReadOnlyList<string> Instances { get; } = new[] { "", "local", "remote" };
    /// <summary>缺失声线可用策略。</summary>
    public IReadOnlyList<MissingVoicePolicy> MissingPolicies { get; } = Enum.GetValues<MissingVoicePolicy>();
    /// <summary>搜索同时匹配身份、正式名称、别名和 OCR 纠错名。</summary>
    public IEnumerable<QuestVoiceManagementSpeaker> FilteredSpeakers => Speakers.Where(x =>
        string.IsNullOrWhiteSpace(SpeakerSearch) || string.Join("\n", x.Id, x.Names, x.Aliases, x.Corrections)
            .Contains(SpeakerSearch.Trim(), StringComparison.OrdinalIgnoreCase));
    /// <summary>可选声线包括清空引用；保存时还会执行严格引用校验。</summary>
    public IEnumerable<string> VoiceIds => new[] { "" }.Concat(Profiles.Select(x => x.OriginalId));
    /// <summary>旅行者可选空、荧和已有角色；只有用户选择预设才补齐角色。</summary>
    public IEnumerable<KeyValuePair<string, string>> TravelerChoices => new[]
        {
            new KeyValuePair<string, string>("", "未指定旅行者"),
            new KeyValuePair<string, string>(FindTraveler("空", "aether"), "空"),
            new KeyValuePair<string, string>(FindTraveler("荧", "lumine"), "荧")
        }.Concat(Speakers.Where(x => !Lines(x.Names).Any(n => n is "空" or "荧"))
            .Select(x => new KeyValuePair<string, string>(x.OriginalId, $"{Lines(x.Names).FirstOrDefault() ?? x.Id}（{x.Id}）")))
        .DistinctBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
    /// <summary>运行、试听和编辑器 IO 任一繁忙都冻结写入。</summary>
    public bool IsEditingAllowed => _owner.IsConfigEditable && !IsWorking;
    /// <summary>有实际草稿才允许编辑内容。</summary>
    public bool CanEditDraft => IsEditingAllowed && IsLoaded;
    /// <summary>未保存提示始终保留在编辑器顶部。</summary>
    public string DraftNotice => HasChanges ? "有未保存草稿：有效输入会自动保存，切换页面保留输入。" : "已保存。修改在下次启动配音时生效；原文件保留备份。";

    /// <summary>绑定共享页面状态，不创建或销毁生产服务。</summary>
    public QuestVoiceManagementViewModel(QuestVoicePageViewModel owner, QuestVoiceSessionFactory factory,
        QuestVoiceService service, RoutingCatalogStore store)
    {
        _owner = owner; _factory = factory; _service = service; _store = store;
        PresetVoices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedPresetVoice));
        owner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(QuestVoicePageViewModel.IsConfigEditable) or nameof(QuestVoicePageViewModel.Status)) RefreshAvailability();
        };
        owner.Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestVoiceConfig.UseRemoteService))
            {
                // 继承实例的旧 Profile 也需立即更新本机浏览及资源状态，但不改绑定。
                NotifyVoiceChoices();
                return;
            }
            if (e.PropertyName != nameof(QuestVoiceConfig.ExtraProviders)) return;
            ObservePresetConfigs();
            ResetPresetVoicesForSelection();
        };
        ObservePresetConfigs();
        InitializeAutoSave();
    }

    /// <summary>本模型字段改变时维护显式草稿状态。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && IsLoaded && e.PropertyName is nameof(PlayerNames) or nameof(TravelerId)
            or nameof(FallbackVoiceId) or nameof(NarratorVoiceId) or nameof(MissingVoicePolicy)) MarkDraftChanged();
        if (e.PropertyName is nameof(IsWorking) or nameof(IsLoaded)) RefreshAvailability();
    }

    /// <summary>同步所有命令以及内容冻结状态。</summary>
    private void RefreshAvailability()
    {
        OnPropertyChanged(nameof(IsEditingAllowed)); OnPropertyChanged(nameof(CanEditDraft));
        LoadCommand.NotifyCanExecuteChanged(); DiscardAndReloadCommand.NotifyCanExecuteChanged();
        NewCatalogCommand.NotifyCanExecuteChanged(); ImportCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged(); ValidateDraftCommand.NotifyCanExecuteChanged();
        AddSpeakerCommand.NotifyCanExecuteChanged(); DeleteSpeakerCommand.NotifyCanExecuteChanged();
        AddProfileCommand.NotifyCanExecuteChanged(); DeleteProfileCommand.NotifyCanExecuteChanged(); PreviewProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>所有写命令都在执行入口再次检查冻结状态。</summary>
    private void RequireEditable(bool requireDraft = true)
    {
        if (!_owner.IsConfigEditable || _service.Status.IsRunning || _service.Status.IsBusy || IsWorking) throw new InvalidOperationException("运行或试听繁忙，请停止后再编辑。");
        if (requireDraft && !IsLoaded) throw new InvalidOperationException("请先加载或新建目录。");
    }

    /// <summary>将错误同时显示在编辑器和页面既有诊断区域。</summary>
    private void Report(Exception exception) { EditStatus = exception.Message; _owner.CommandError = exception.Message; }

    /// <summary>统一执行编辑命令，将异常转为可见诊断。</summary>
    private void Edit(Action action)
    {
        try { RequireEditable(); _owner.CommandError = ""; action(); } catch (Exception exception) { Report(exception); }
    }

    /// <summary>文件操作期间冻结编辑并保证异常不会离开异步命令。</summary>
    private async Task FileOperation(Func<Task> action)
    {
        var started = false;
        try { RequireEditable(false); _owner.CommandError = ""; IsWorking = true; started = true; await _saveGate.WaitAsync(); try { await action(); } finally { _saveGate.Release(); } }
        catch (OperationCanceledException) { EditStatus = "操作已取消，草稿保留。"; }
        catch (Exception exception) { Report(exception); }
        finally { if (started) IsWorking = false; }
    }

    /// <summary>不隐式丢弃未保存内容。</summary>
    private void RequireCleanDraft()
    {
        if (HasChanges) throw new InvalidOperationException("存在未保存草稿。请先保存，或明确点击“放弃草稿并重新加载”。");
    }

    /// <summary>显式破坏性操作使用宿主主题确认；无窗口的组件测试不弹出界面。</summary>
    private static bool ConfirmChange(string message) => Application.Current?.MainWindow is null
        || ThemedMessageBox.Question(message, "确认配置操作", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>读取当前配置路径，不覆盖已有未保存输入。</summary>
    [RelayCommand(CanExecute = nameof(IsEditingAllowed))]
    private Task LoadAsync() => FileOperation(async () =>
    {
        RequireCleanDraft();
        var snapshot = await _store.LoadAsync(_factory.RoutingCatalogPath, serverSidePaths: _factory.RoutingServerSidePaths);
        Apply(snapshot.Catalog); _loadedPath = snapshot.CatalogPath; _expectedSha256 = snapshot.CatalogSha256;
        EditStatus = "已加载：" + snapshot.CatalogPath;
    });

    /// <summary>用户明确点击此命令才允许丢弃草稿；读取失败仍保留旧草稿。</summary>
    [RelayCommand(CanExecute = nameof(IsEditingAllowed))]
    private Task DiscardAndReloadAsync() => FileOperation(async () =>
    {
        if (HasChanges && !ConfirmChange("确定放弃未保存草稿并重新加载吗？")) return;
        var snapshot = await _store.LoadAsync(_factory.RoutingCatalogPath, serverSidePaths: _factory.RoutingServerSidePaths);
        Apply(snapshot.Catalog); _loadedPath = snapshot.CatalogPath; _expectedSha256 = snapshot.CatalogSha256;
        EditStatus = "已放弃旧草稿并重新加载。";
    });

    /// <summary>仅允许在目标文件不存在时明确新建，避免绕过文件乐观锁。</summary>
    [RelayCommand(CanExecute = nameof(IsEditingAllowed))]
    private void NewCatalog()
    {
        try
        {
            RequireEditable(false); RequireCleanDraft();
            var path = _factory.RoutingCatalogPath;
            if (File.Exists(path)) throw new InvalidOperationException("目标文件已存在，请先加载，不能直接覆盖新建。");
            Apply(new(2, "genshin", null, Array.Empty<string>(), null, null, MissingVoicePolicy.Fallback,
                Array.Empty<SpeakerDefinition>(), Array.Empty<RoutingVoice>()));
            _loadedPath = path; _expectedSha256 = null; MarkDraftChanged(); EditStatus = "已新建空目录草稿，请添加角色和声线后保存。";
        }
        catch (Exception exception) { Report(exception); }
    }

    /// <summary>先读取当前目标摘要，再导入其他文件；导入不会写目标文件。</summary>
    [RelayCommand(CanExecute = nameof(IsEditingAllowed))]
    private Task ImportAsync() => FileOperation(async () =>
    {
        RequireCleanDraft();
        var dialog = new OpenFileDialog { Title = "导入角色路由草稿", Filter = "JSON 文件 (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var target = _factory.RoutingCatalogPath;
        var targetHash = File.Exists(target) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target))) : null;
        var imported = await _store.LoadAsync(dialog.FileName, serverSidePaths: _factory.RoutingServerSidePaths);
        if (!ConfirmChange("导入后有效内容将自动保存到当前路由目录并保留备份，确定继续吗？")) return;
        Apply(imported.Catalog); _loadedPath = target; _expectedSha256 = targetHash; MarkDraftChanged();
        EditStatus = "已导入草稿；校验通过后自动保存并保留备份。";
    });

    /// <summary>只保存当前真正加载的路径；存储层校验并保留旧文件备份。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private async Task SaveAsync() => await FlushAutoSaveAsync();

    /// <summary>导出独立文件，不清除当前目录未保存提示。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private Task ExportAsync() => FileOperation(async () =>
    {
        var dialog = new SaveFileDialog { Title = "导出路由草稿", Filter = "JSON 文件 (*.json)|*.json", FileName = "routing.export.json", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_factory.RoutingCatalogPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("导出目标不能是当前配置文件，请使用保存命令。");
        var hash = File.Exists(dialog.FileName) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dialog.FileName))) : null;
        await _store.SaveAsync(dialog.FileName, BuildCatalog(), hash, serverSidePaths: _factory.RoutingServerSidePaths);
        EditStatus = "已导出草稿；当前配置的有效修改继续自动保存。";
    });

    /// <summary>校验内存草稿，不读取旧 JSON，不启动服务或发起网络合成。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void ValidateDraft() => Edit(() => { _store.Validate(BuildCatalog()); EditStatus = "草稿结构、Provider 和引用校验通过；实际资产及连接请试听检查。"; });

    /// <summary>新增明确的角色草稿，不自动保存。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void AddSpeaker() => Edit(() =>
    {
        // 未保存的旧身份仍供引用迁移使用，必须与当前身份一起预留。
        var id = UniqueId("speaker", Speakers.SelectMany(x => new[] { x.Id, x.OriginalId }));
        var item = new QuestVoiceManagementSpeaker(new(id, new[] { id }, Array.Empty<string>(), Array.Empty<string>(), null));
        AddSpeakerItem(item); SelectedSpeaker = item; MarkDraftChanged(); NotifyLists();
    });

    /// <summary>拒绝删除当前旅行者，用户须先明确更改旅行者选择。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void DeleteSpeaker() => Edit(() =>
    {
        if (SelectedSpeaker is not { } item) return;
        if (Same(TravelerId, item.Id) || Same(TravelerId, item.OriginalId)) throw new InvalidOperationException("当前角色是旅行者，请先更改旅行者选择。");
        if (!ConfirmChange("确定删除角色“" + item.Names + "”吗？")) return;
        item.PropertyChanged -= OnSpeakerChanged; Speakers.Remove(item); SelectedSpeaker = null; MarkDraftChanged(); NotifyLists();
    });

    /// <summary>新增普通 profile，从当前注册引擎和实例开始填写。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void AddProfile() => Edit(() =>
    {
        // 预留草稿旧身份，避免重命名后新增的 profile 抢占同一个引用键。
        var id = UniqueId("voice", Profiles.SelectMany(x => new[] { x.Id, x.OriginalId }));
        var item = new QuestVoiceManagementProfile(new(id, id, "", "", "", "", ProviderId: _owner.Config.ProviderId,
            InstanceId: null));
        AddProfileItem(item); SelectedProfile = item; MarkDraftChanged(); NotifyLists();
    });

    /// <summary>任何角色、NPC 兜底或旁白引用都阻止删除 profile。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void DeleteProfile() => Edit(() =>
    {
        if (SelectedProfile is not { } item) return;
        bool References(string? value) => Same(value, item.Id) || Same(value, item.OriginalId);
        var references = Speakers.Where(x => References(x.VoiceId)).Select(x => Same(x.OriginalId, TravelerId) ? "旅行者" : x.Names).ToList();
        if (References(FallbackVoiceId)) references.Add("未知 NPC");
        if (References(NarratorVoiceId)) references.Add("旁白");
        if (references.Count > 0) throw new InvalidOperationException("此声线仍被引用：" + string.Join("、", references) + "。请先解除绑定。");
        if (!ConfirmChange("确定删除声线“" + item.Name + "”吗？")) return;
        item.PropertyChanged -= OnProfileChanged; Profiles.Remove(item); SelectedProfile = null; MarkDraftChanged(); NotifyLists();
    });

    /// <summary>只试听选中草稿，直接走生产服务互斥、取消和排空链。</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private async Task PreviewProfileAsync(CancellationToken token)
    {
        try
        {
            RequireEditable(); _owner.CommandError = "";
            if (SelectedProfile is null) throw new InvalidOperationException("请先选择声线。");
            await _service.TestVoiceAsync(PreviewText, BuildProfile(SelectedProfile), token);
            EditStatus = "选中声线试听结束；有效配置独立自动保存。";
        }
        catch (OperationCanceledException) { EditStatus = "试听已取消，草稿保留。"; }
        catch (Exception exception) { Report(exception); }
    }

    /// <summary>复用现有取消入口，繁忙状态下仍可执行。</summary>
    [RelayCommand]
    private void CancelPreview() => _service.CancelTestSpeech();

    /// <summary>选择旅行者预设是唯一自动补齐空或荧的入口。</summary>
    partial void OnTravelerIdChanged(string value)
    {
        if (_loading || !CanEditDraft || string.IsNullOrWhiteSpace(value) || Speakers.Any(x => Same(x.Id, value))) return;
        if (value is not ("aether" or "lumine")) return;
        var name = value == "aether" ? "空" : "荧";
        AddSpeakerItem(new(new(value, new[] { name }, Array.Empty<string>(), Array.Empty<string>(), null)));
        MarkDraftChanged(); NotifyLists();
    }

    /// <summary>切换选中声线只刷新预设列表，各自输入对象保持不变。</summary>
    partial void OnSelectedProfileChanged(QuestVoiceManagementProfile? value)
    {
        OnPropertyChanged(nameof(SelectedGptProfile));
        OnPropertyChanged(nameof(HasSelectedGptProfile));
        OnPropertyChanged(nameof(SelectedProfileDiagnostic));
        OnPropertyChanged(nameof(CanBrowseSelectedProfileAssets));
        OnPropertyChanged(nameof(SelectedPresetVoice));
        ResetPresetVoicesForSelection();
    }

    /// <summary>字典补齐后重挂嵌套通知，支持profile与全局默认引擎不同。</summary>
    private void ObservePresetConfigs()
    {
        if (_owner.Config.ExtraProviders is null) _factory.ExtraConfig("windows-sapi");
        var configurations = _owner.Config.ExtraProviders!;
        var current = configurations.Values.Where(x => x is not null).ToHashSet();
        foreach (var item in _observedPresetConfigs.Except(current).ToArray())
        {
            item.PropertyChanged -= OnPresetInstanceChanged;
            _observedPresetConfigs.Remove(item);
        }
        foreach (var item in current.Except(_observedPresetConfigs))
        {
            item.PropertyChanged += OnPresetInstanceChanged;
            _observedPresetConfigs.Add(item);
        }
    }

    /// <summary>连接输入改变立即废弃旧结果，但不会自动查询服务。</summary>
    private void OnPresetInstanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SelectedProfile is null || sender != _factory.ExtraConfig(SelectedProfile.ProviderId)) return;
        // 同Provider的管理声线也同步模型对应的静态建议，不改变自定义VoiceId。
        if (SelectedProfile.ProviderId == "openai-speech" && e.PropertyName == nameof(QuestVoiceEngineConfig.Model))
            ResetPresetVoicesForSelection();
        if (e.PropertyName is nameof(QuestVoiceEngineConfig.LocalUrl) or nameof(QuestVoiceEngineConfig.RemoteUrl)
            or nameof(QuestVoiceEngineConfig.UseRemote)) ResetPresetVoicesForSelection();
    }

    /// <summary>自动枚举仅用于无需联网的列表，VOICEVOX与VITS由刷新按钮触发。</summary>
    private void ResetPresetVoicesForSelection()
    {
        Interlocked.Increment(ref _presetRequestGeneration);
        PresetVoices.Clear();
        if (SelectedProfile is { } item && QuestVoicePageViewModel.CanEnumerateVoicesOffline(item.ProviderId))
            _ = RefreshPresetVoicesAsync();
    }

    /// <summary>按当前 profile 的注册 Provider 获取预设，而非依赖全局默认引擎。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshPresetVoicesAsync()
    {
        var generation = Interlocked.Increment(ref _presetRequestGeneration);
        var item = SelectedProfile; PresetVoices.Clear();
        if (item is null) return;
        var id = item.ProviderId;
        var instance = item.InstanceId;
        Uri? address = null;
        string? resolvedInstance = null;
        // 草稿对象、刷新代次和生产解析出的实例URL共同确定查询身份。
        bool IsCurrent()
        {
            if (generation != Volatile.Read(ref _presetRequestGeneration) || SelectedProfile != item || item.ProviderId != id || item.InstanceId != instance) return false;
            if (address is null) return true;
            try
            {
                var current = _factory.CreateVoiceSettings(BuildProfile(item), validateProvider: false);
                return current.TtsServiceUrl == address && current.Provider?.InstanceId == resolvedInstance;
            }
            catch { return false; }
        }
        try
        {
            using var provider = TtsProviderRegistry.Create(id);
            if (!provider.Info.Capabilities.PresetVoices) return;
            // 预设只依赖选中 profile 的引擎和实例，不能被全局默认 GPT 地址阻断。
            var settings = _factory.CreateVoiceSettings(BuildProfile(item), validateProvider: false);
            address = settings.TtsServiceUrl;
            resolvedInstance = settings.Provider?.InstanceId;
            var voices = await provider.GetVoicesAsync(settings, default);
            if (!IsCurrent()) return;
            foreach (var voice in voices) PresetVoices.Add(voice);
        }
        catch (Exception exception)
        {
            // 切换角色或引擎后，旧查询的失败也不能覆盖当前选择的状态。
            if (IsCurrent()) Report(exception);
        }
    }

    /// <summary>用各自草稿构造 v2 目录，并迁移用户重命名的身份引用。</summary>
    private RoutingCatalog BuildCatalog()
    {
        string? VoiceReference(string value) => Optional(Profiles.FirstOrDefault(x => Same(x.OriginalId, value))?.Id ?? value);
        var traveler = Speakers.FirstOrDefault(x => Same(x.OriginalId, TravelerId))?.Id ?? TravelerId;
        return new(2, "genshin", Optional(traveler), Lines(PlayerNames), VoiceReference(NarratorVoiceId),
            VoiceReference(FallbackVoiceId), MissingVoicePolicy,
            Speakers.Select(x => new SpeakerDefinition(x.Id.Trim(), Lines(x.Names), Lines(x.Aliases), Lines(x.Corrections), VoiceReference(x.VoiceId))),
            Profiles.Select(BuildProfile));
    }

    /// <summary>本地草稿相对路径始终以已加载目录为基准，导出和试听不能随目标或工作目录漂移。</summary>
    private RoutingVoice BuildProfile(QuestVoiceManagementProfile item)
    {
        var voice = item.Build();
        var usesAssets = TtsProviderRegistry.GetInfo(voice.ProviderId).Capabilities.VoiceAssets;
        var remote = voice.InstanceId is null ? _factory.RoutingServerSidePaths : voice.InstanceId == "remote";
        if (!usesAssets || remote) return voice;
        var directory = Path.GetDirectoryName(Path.GetFullPath(_loadedPath ?? _factory.RoutingCatalogPath))!;
        string Asset(string path) => string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path, directory);
        return voice with
        {
            GptWeightsPath = Asset(voice.GptWeightsPath), SovitsWeightsPath = Asset(voice.SovitsWeightsPath),
            ReferenceAudioPath = Asset(voice.ReferenceAudioPath)
        };
    }
    /// <summary>成功读取后整体替换草稿；文件失败前不触碰旧输入。</summary>
    private void Apply(RoutingCatalog catalog)
    {
        _loading = true;
        try
        {
            SelectedSpeaker = null; SelectedProfile = null;
            foreach (var item in Speakers) item.PropertyChanged -= OnSpeakerChanged;
            foreach (var item in Profiles) item.PropertyChanged -= OnProfileChanged;
            Speakers.Clear(); Profiles.Clear();
            foreach (var item in catalog.Speakers) AddSpeakerItem(new(item));
            foreach (var item in catalog.Voices) AddProfileItem(new(item));
            PlayerNames = string.Join("\n", catalog.PlayerNames); TravelerId = catalog.ProtagonistId ?? "";
            FallbackVoiceId = catalog.FallbackVoiceId ?? ""; NarratorVoiceId = catalog.NarratorVoiceId ?? "";
            MissingVoicePolicy = catalog.MissingVoicePolicy; IsLoaded = true; HasChanges = false; _savedProfiles = Profiles.ToDictionary(x => x.OriginalId, x => x.Build()); NotifyLists();
        }
        finally { _loading = false; }
    }

    /// <summary>挂接角色行级草稿通知。</summary>
    private void AddSpeakerItem(QuestVoiceManagementSpeaker item) { item.PropertyChanged += OnSpeakerChanged; Speakers.Add(item); }
    /// <summary>挂接声线行级草稿通知。</summary>
    private void AddProfileItem(QuestVoiceManagementProfile item) { item.PropertyChanged += OnProfileChanged; Profiles.Add(item); }
    /// <summary>角色改动保留草稿并刷新搜索和选择列表。</summary>
    private void OnSpeakerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading) MarkDraftChanged();
        OnPropertyChanged(nameof(TravelerVoiceId));
        if (e.PropertyName is nameof(QuestVoiceManagementSpeaker.Id) or nameof(QuestVoiceManagementSpeaker.Names))
            OnPropertyChanged(nameof(TravelerChoices));
    }
    /// <summary>Provider 改动刷新独立预设，其他字段也保持未保存提示。</summary>
    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading) MarkDraftChanged();
        NotifyVoiceChoices();
        if (sender == SelectedProfile && e.PropertyName == nameof(QuestVoiceManagementProfile.ProviderVoiceId)) OnPropertyChanged(nameof(SelectedPresetVoice));
        if (sender == SelectedProfile && e.PropertyName is nameof(QuestVoiceManagementProfile.ProviderId)
            or nameof(QuestVoiceManagementProfile.InstanceId)) ResetPresetVoicesForSelection();
    }
    /// <summary>通知计算列表，避免创建第二份会丢编辑的角色集合。</summary>
    private void NotifyLists() { NotifyVoiceChoices(); OnPropertyChanged(nameof(FilteredSpeakers)); OnPropertyChanged(nameof(VoiceIds)); OnPropertyChanged(nameof(TravelerChoices)); }
    /// <summary>复用已存在的空或荧角色身份。</summary>
    private string FindTraveler(string name, string fallback) => Speakers.FirstOrDefault(x => Lines(x.Names).Contains(name))?.OriginalId ?? fallback;
    /// <summary>新行使用不会冲突的短身份。</summary>
    private static string UniqueId(string prefix, IEnumerable<string> existing)
    {
        var ids = existing.ToHashSet(StringComparer.OrdinalIgnoreCase); var index = 1;
        while (ids.Contains(prefix + index)) index++;
        return prefix + index;
    }
    /// <summary>集合采用一行一项，保留名称内部标点。</summary>
    private static string[] Lines(string value) => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    /// <summary>可选引用将空白归一为空值。</summary>
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    /// <summary>引用比较与目录校验使用相同的不区分大小写和边缘空白规则。</summary>
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
