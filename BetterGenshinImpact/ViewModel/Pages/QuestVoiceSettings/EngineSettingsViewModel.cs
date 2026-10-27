using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>独立引擎设置的交互边界；只构造显式声线快照，不修改全局引擎。</summary>
public abstract partial class EngineSettingsViewModel : ObservableObject
{
    /// <summary>与运行页面共用的配置宿主。</summary>
    private readonly QuestVoicePageViewModel _host;
    /// <summary>生产配置快照工厂。</summary>
    private readonly QuestVoiceSessionFactory _factory;
    /// <summary>拥有播放器及试听互斥的生产服务。</summary>
    private readonly QuestVoiceService _service;
    /// <summary>当前订阅的独立配置身份。</summary>
    private QuestVoiceEngineConfig? _observedConfig;
    /// <summary>首次展示声线库时才订阅共享管理器，不主动读取目录。</summary>
    private QuestVoiceManagementViewModel? _observedManagement;
    /// <summary>拒绝切换页面、实例或配置后迟到的异步结果。</summary>
    private int _generation;
    /// <summary>当前引擎是否正在展示。</summary>
    private bool _active;
    /// <summary>固定引擎标识，与全局默认引擎无关。</summary>
    public string ProviderId { get; }
    /// <summary>注册表提供的真实名称及能力。</summary>
    public TtsProviderInfo Provider => TtsProviderRegistry.GetInfo(ProviderId);
    /// <summary>绑定现有持久化配置，不建立第二套保存机制。</summary>
    public QuestVoiceEngineConfig? EngineConfig => _factory.ExtraConfig(ProviderId);
    /// <summary>共用声线目录的唯一管理器，统一承担保存和引用删除保护。</summary>
    public QuestVoiceManagementViewModel Management
    {
        get
        {
            var management = _host.Management;
            if (!ReferenceEquals(_observedManagement, management))
            {
                if (_observedManagement is not null)
                {
                    _observedManagement.PropertyChanged -= OnManagementChanged;
                    _observedManagement.Profiles.CollectionChanged -= OnRegisteredProfilesChanged;
                }
                _observedManagement = management;
                management.PropertyChanged += OnManagementChanged;
                management.Profiles.CollectionChanged += OnRegisteredProfilesChanged;
            }
            return management;
        }
    }
    /// <summary>当前引擎的目录条目；切换只过滤展示，不更改已存引用。</summary>
    public IEnumerable<QuestVoiceManagementProfile> RegisteredProfiles => Management.Profiles.Where(x => x.ProviderId == ProviderId);
    /// <summary>过滤共享选择，防止其他引擎的条目进入当前编辑器。</summary>
    public QuestVoiceManagementProfile? SelectedRegisteredProfile
    {
        get => Management.SelectedProfile is { } profile && profile.ProviderId == ProviderId ? profile : null;
        set
        {
            // 列表重建时忽略暂时空值，其他引擎选择绝不由本编辑器改写。
            if (value is not null && value.ProviderId == ProviderId && Management.Profiles.Contains(value))
                Management.SelectedProfile = value;
        }
    }
    /// <summary>删除仅对当前引擎、允许编辑的已选条目开放。</summary>
    public bool CanDeleteRegisteredProfile => Management.CanEditDraft && SelectedRegisteredProfile is not null;
    /// <summary>用户明确刷新或静态枚举得到的列表。</summary>
    public ObservableCollection<TtsVoiceInfo> Voices { get; } = new();
    /// <summary>声音 ID 可独立于建议列表保存。</summary>
    public string VoiceId
    {
        get => EngineConfig?.VoiceId ?? _host.Config.HuoshanVoiceId;
        set
        {
            if (value is null) return;
            if (EngineConfig is { } config)
            {
                config.VoiceId = value;
                if (ProviderId == "google-translate") config.Language = value;
            }
            else if (!string.IsNullOrWhiteSpace(value)) _host.Config.HuoshanVoiceId = value;
        }
    }
    /// <summary>忽略刷新产生的暂时空选择，保留已有声音 ID。</summary>
    public TtsVoiceInfo? SelectedVoice
    {
        get => Voices.FirstOrDefault(x => x.Id == VoiceId);
        set { if (value is not null) VoiceId = value.Id; }
    }
    /// <summary>可编辑本地地址草稿，非法值不进入生产配置。</summary>
    [ObservableProperty] private string _localUrlDraft = "";
    /// <summary>可编辑远程地址草稿，与本地地址独立保存。</summary>
    [ObservableProperty] private string _remoteUrlDraft = "";
    /// <summary>地址校验提示不包含用户输入或密钥。</summary>
    [ObservableProperty] private string _urlValidationMessage = "";
    /// <summary>当前命令的轻量反馈，不回显服务异常正文。</summary>
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>刷新列表的界面状态。</summary>
    [ObservableProperty] private bool _isRefreshing;
    /// <summary>本设置项拥有的试听状态。</summary>
    [ObservableProperty] private bool _isTesting;
    /// <summary>用户明确点击后才发送的试听文本。</summary>
    [ObservableProperty] private string _testText = "你好，旅行者。欢迎来到提瓦特。";
    /// <summary>加入通用声线库时使用的自由显示名；留空时采用已选声音名称。</summary>
    [ObservableProperty] private string _voiceDisplayName = "";
    /// <summary>默认折叠声线库，首次展开才读取路由目录。</summary>
    [ObservableProperty] private bool _isLibraryExpanded;
    /// <summary>正式会话、排空或其他试听期间不启动新的试听。</summary>
    public bool CanTest => !_host.Status.IsRunning && !_host.Status.IsBusy && !_host.Status.BackendUnknown && !IsTesting;

    /// <summary>建立独立配置订阅；构造期间不访问网络。</summary>
    protected EngineSettingsViewModel(string providerId, QuestVoicePageViewModel host,
        QuestVoiceSessionFactory factory, QuestVoiceService service)
    {
        ProviderId = providerId;
        _host = host;
        _factory = factory;
        _service = service;
        _host.Config.PropertyChanged += OnHostConfigChanged;
        _host.PropertyChanged += OnHostStatusChanged;
        ObserveConfig();
    }

    /// <summary>切换仅使旧结果失效；静态列表和系统声音允许本机读取。</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Invalidate();
        OnPropertyChanged(nameof(SelectedRegisteredProfile));
        if (active && IsLibraryExpanded) _ = EnsureRegisteredProfilesAsync();
        if (active && ProviderId is "huoshan" or "windows-sapi" or "openai-speech" or "google-translate")
            _ = RefreshVoicesAsync();
    }

    /// <summary>替换配置字典时重新订阅，保留对新实例的身份判断。</summary>
    private void ObserveConfig()
    {
        var current = EngineConfig;
        if (ReferenceEquals(_observedConfig, current)) return;
        if (_observedConfig is not null) _observedConfig.PropertyChanged -= OnEngineConfigChanged;
        _observedConfig = current;
        if (current is not null) current.PropertyChanged += OnEngineConfigChanged;
        LocalUrlDraft = current?.LocalUrl ?? "";
        RemoteUrlDraft = current?.RemoteUrl ?? "";
        Invalidate();
        Voices.Clear();
        OnPropertyChanged(nameof(EngineConfig));
        NotifyVoice();
    }

    /// <summary>全局默认引擎的改变不影响此页面的下拉选择。</summary>
    private void OnHostConfigChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(QuestVoiceConfig.ExtraProviders)) ObserveConfig();
        if (ProviderId == "huoshan" && args.PropertyName == nameof(QuestVoiceConfig.HuoshanVoiceId))
        { Invalidate(); NotifyVoice(); }
    }

    /// <summary>运行状态由宿主投递到 UI 线程，只更新试听可用性。</summary>
    private void OnHostStatusChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(QuestVoicePageViewModel.Status)) return;
        OnPropertyChanged(nameof(CanTest));
        TestVoiceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>所有参数改变均拒绝旧异步结果，网络声音只显式刷新。</summary>
    private void OnEngineConfigChanged(object? sender, PropertyChangedEventArgs args)
    {
        Invalidate();
        if (args.PropertyName == nameof(QuestVoiceEngineConfig.LocalUrl)) LocalUrlDraft = EngineConfig?.LocalUrl ?? "";
        if (args.PropertyName == nameof(QuestVoiceEngineConfig.RemoteUrl)) RemoteUrlDraft = EngineConfig?.RemoteUrl ?? "";
        if (args.PropertyName is nameof(QuestVoiceEngineConfig.LocalUrl) or nameof(QuestVoiceEngineConfig.RemoteUrl)
            or nameof(QuestVoiceEngineConfig.UseRemote) or nameof(QuestVoiceEngineConfig.ApiKey)) Voices.Clear();
        NotifyVoice();
        // OpenAI 的声音是本地建议；模型变化时保留 VoiceId 并刷新建议。
        if (_active && ProviderId == "openai-speech" && args.PropertyName == nameof(QuestVoiceEngineConfig.Model))
            _ = RefreshVoicesAsync();
    }

    /// <summary>输入延迟由视图绑定控制，此处只验证和保存合法根地址。</summary>
    partial void OnLocalUrlDraftChanged(string value) => SaveUrl(value, false);
    /// <summary>远程空草稿允许保留，但不能覆盖已保存的有效地址。</summary>
    partial void OnRemoteUrlDraftChanged(string value) => SaveUrl(value, true);
    /// <summary>名称编辑使此前注册操作的迟到界面提示失效，不改写既有 profile。</summary>
    partial void OnVoiceDisplayNameChanged(string value) => Invalidate();
    /// <summary>用户展开时才准备目录，避免仅编辑引擎参数就读取路由。</summary>
    partial void OnIsLibraryExpandedChanged(bool value)
    { if (value) _ = EnsureRegisteredProfilesAsync(); }

    /// <summary>共享目录加载完成后的提示仍受页面代次与配置身份保护。</summary>
    private async Task EnsureRegisteredProfilesAsync()
    {
        var generation = Interlocked.Increment(ref _generation);
        var identity = EngineConfig;
        IsRefreshing = false;
        try
        {
            await Management.EnsureLoadedAsync();
            if (!IsCurrent(generation, identity)) return;
            NotifyRegisteredProfiles();
            if (!Management.IsLoaded) StatusMessage = "声线库尚未加载，请检查映射目录。";
        }
        catch (Exception)
        { if (IsCurrent(generation, identity)) StatusMessage = "读取声线库失败，请检查映射目录是否可读。"; }
    }

    /// <summary>目录的条目增删同步列表，数据始终来自共享管理器。</summary>
    private void OnRegisteredProfilesChanged(object? sender, NotifyCollectionChangedEventArgs args) => NotifyRegisteredProfiles();

    /// <summary>共享选择、编辑权限或条目名称变化后刷新当前引擎的投影。</summary>
    private void OnManagementChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(QuestVoiceManagementViewModel.SelectedProfile)
            or nameof(QuestVoiceManagementViewModel.CanEditDraft) or nameof(QuestVoiceManagementViewModel.VoiceChoices))
            NotifyRegisteredProfiles();
    }

    /// <summary>只更新目录展示及删除可用性，不写入任何引用。</summary>
    private void NotifyRegisteredProfiles()
    {
        OnPropertyChanged(nameof(RegisteredProfiles));
        OnPropertyChanged(nameof(SelectedRegisteredProfile));
        OnPropertyChanged(nameof(CanDeleteRegisteredProfile));
        DeleteRegisteredProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>重新核对当前引擎选择后执行管理器删除，保留其引用防护。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteRegisteredProfile))]
    private void DeleteRegisteredProfile()
    {
        if (SelectedRegisteredProfile is not { } profile || profile.ProviderId != ProviderId) return;
        Management.SelectedProfile = profile;
        if (Management.DeleteProfileCommand.CanExecute(null)) Management.DeleteProfileCommand.Execute(null);
    }
    /// <summary>拒绝凭据、查询和片段，避免密钥写入 URL。</summary>
    private void SaveUrl(string value, bool remote)
    {
        Invalidate();
        if (EngineConfig is not { } config) return;
        var valid = IsValidUrl(value);
        if (valid)
        {
            if (remote) config.RemoteUrl = value.Trim(); else config.LocalUrl = value.Trim();
        }
        UrlValidationMessage = (!IsValidUrl(LocalUrlDraft) || (!string.IsNullOrWhiteSpace(RemoteUrlDraft) && !IsValidUrl(RemoteUrlDraft)))
            ? "请填写 HTTP/HTTPS 根地址，不含凭据、查询或片段；非法输入仅保留为草稿。" : "";
    }

    /// <summary>与生产 HTTP Provider 的根路径规则保持一致。</summary>
    private static bool IsValidUrl(string value) => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    /// <summary>地址草稿尚未有效时，避免拿旧地址冒充当前输入进行请求。</summary>
    private bool HasValidActiveUrl() => !Provider.Capabilities.ServerConnection
        || IsValidUrl(EngineConfig?.UseRemote == true ? RemoteUrlDraft : LocalUrlDraft);

    /// <summary>代次和配置对象身份共同保护异步提交。</summary>
    private bool IsCurrent(int generation, QuestVoiceEngineConfig? identity) => _active
        && generation == Volatile.Read(ref _generation) && ReferenceEquals(identity, EngineConfig);

    /// <summary>使旧结果无效，但不取消由生产服务拥有的后台收尾。</summary>
    private void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        IsRefreshing = false;
    }

    /// <summary>同步声音 ID 与下拉展示，不将未知 ID 清空。</summary>
    private void NotifyVoice()
    { OnPropertyChanged(nameof(VoiceId)); OnPropertyChanged(nameof(SelectedVoice)); }

    /// <summary>显式 Provider、实例及声音，确保测试不切换全局默认引擎。</summary>
    private RoutingVoice CreateProfile() => new("settings-preview-" + ProviderId, Provider.DisplayName, "", "", "", "",
        ProviderId: ProviderId, InstanceId: Provider.Capabilities.ServerConnection && EngineConfig?.UseRemote == true ? "remote" : "local",
        ProviderVoiceId: VoiceId);

    /// <summary>把当前显式声音交给通用管理器创建或复用 profile，不更新其他 profile 引用。</summary>
    [RelayCommand]
    private async Task RegisterVoiceAsync()
    {
        if (!HasValidActiveUrl()) { StatusMessage = "请先修正当前实例的服务地址。"; return; }
        var generation = Interlocked.Increment(ref _generation);
        var identity = EngineConfig;
        IsRefreshing = false;
        try
        {
            // 注册前沿用生产参数校验；该步骤不联网，也不会修改默认引擎。
            var profile = CreateProfile();
            _factory.CreateVoiceSettings(profile);
            var displayName = string.IsNullOrWhiteSpace(VoiceDisplayName)
                ? SelectedVoice?.DisplayName ?? VoiceId : VoiceDisplayName.Trim();
            await _host.Management.RegisterProviderVoiceAsync(ProviderId, profile.InstanceId!, VoiceId, displayName);
            if (IsCurrent(generation, identity)) StatusMessage = "已加入通用声线库，可在角色与默认声音规则中选择。";
        }
        catch (Exception)
        {
            if (IsCurrent(generation, identity)) StatusMessage = "加入声线库失败，请检查声音与实例参数，以及映射目录是否可写。";
        }
    }

    /// <summary>复用现有 Provider 枚举能力；不在视图模型编写 HTTP。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshVoicesAsync()
    {
        if (!_active) return;
        if (!HasValidActiveUrl()) { StatusMessage = "请先修正当前实例的服务地址。"; return; }
        var generation = Interlocked.Increment(ref _generation);
        var identity = EngineConfig;
        IsRefreshing = true;
        StatusMessage = "正在读取声音列表…";
        try
        {
            var settings = _factory.CreateVoiceSettings(CreateProfile(), validateProvider: false);
            using var provider = TtsProviderRegistry.Create(ProviderId);
            var voices = await provider.GetVoicesAsync(settings, default);
            if (!IsCurrent(generation, identity)) return;
            Voices.Clear();
            foreach (var voice in voices) Voices.Add(voice);
            NotifyVoice();
            StatusMessage = ProviderId == "openai-speech" ? "已更新本地声音建议；连接与认证请通过试听验证。"
                : $"已读取 {voices.Count} 项；保留原声音 ID，实际合成请试听。";
        }
        catch (Exception)
        {
            // 服务异常可能携带认证头或响应正文，因此只显示固定诊断。
            if (IsCurrent(generation, identity)) StatusMessage = "读取声音失败，请检查实例地址、服务状态及认证设置。";
        }
        finally { if (IsCurrent(generation, identity)) IsRefreshing = false; }
    }

    /// <summary>通过生产服务试听显式声线，复用互斥和本机播放器。</summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestVoiceAsync(CancellationToken token)
    {
        if (!HasValidActiveUrl()) { StatusMessage = "请先修正当前实例的服务地址。"; return; }
        var generation = Interlocked.Increment(ref _generation);
        var identity = EngineConfig;
        IsRefreshing = false;
        IsTesting = true;
        StatusMessage = "正在试听…";
        try
        {
            await _service.TestVoiceAsync(TestText, CreateProfile(), token);
            if (IsCurrent(generation, identity)) StatusMessage = "试听播放完成。";
        }
        catch (OperationCanceledException)
        { if (IsCurrent(generation, identity)) StatusMessage = "试听已取消。"; }
        catch (Exception)
        { if (IsCurrent(generation, identity)) StatusMessage = "试听失败，请检查声音、服务参数及连接；自动配音需先停止。"; }
        finally { IsTesting = false; }
    }

    /// <summary>本设置项启动试听后可立即请求停止播放。</summary>
    [RelayCommand(CanExecute = nameof(IsTesting))]
    private void CancelTestVoice() => _service.CancelTestSpeech();

    /// <summary>命令状态同步试听拥有权，完成时始终允许重新测试。</summary>
    partial void OnIsTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTest));
        TestVoiceCommand.NotifyCanExecuteChanged();
        CancelTestVoiceCommand.NotifyCanExecuteChanged();
    }
}
