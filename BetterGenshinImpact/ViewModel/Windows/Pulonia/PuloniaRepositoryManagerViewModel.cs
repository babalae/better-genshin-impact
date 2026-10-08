using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meziantou.Framework.Win32;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>紧凑仓库管理窗口的配置和操作状态，渠道与旧官方窗口双向同步。</summary>
public partial class PuloniaRepositoryManagerViewModel : ViewModel
{
    /// <summary>仓库配置与固定目录更新服务。</summary>
    private readonly PuloniaRepositoryManagementService _service;
    /// <summary>首次打开时定位添加任务页面选择的仓库。</summary>
    private readonly string? _initialRepositoryId;
    /// <summary>配置同步期间不反向写回相同的渠道信息。</summary>
    private bool _applyingConfig;
    /// <summary>关闭后不再展示后台进度或错误对话框。</summary>
    private bool _closed;
    /// <summary>窗口初始化及操作的取消源，由对应请求结束时释放。</summary>
    private CancellationTokenSource? _operationCancellation;
    /// <summary>旧窗口使用的凭据管理器键，不把认证令牌写入仓库 JSON。</summary>
    private const string CredentialName = "BetterGenshinImpact.GitCredentials";

    /// <summary>可以引用的仓库，官方始终排在首位。</summary>
    [ObservableProperty] private ObservableCollection<ScriptRepositoryRegistration> _repositories = [];
    /// <summary>两个窗口共用的短来源展示，不影响表单和服务所使用的原始注册对象。</summary>
    [ObservableProperty] private ObservableCollection<PuloniaRepositoryChoiceViewModel> _repositoryChoices = [];
    /// <summary>当前查看或编辑的仓库。</summary>
    [ObservableProperty] private ScriptRepositoryRegistration? _selectedRepository;
    /// <summary>当前在填写尚未保存的第三方配置。</summary>
    [ObservableProperty] private bool _isAddingRemote;
    /// <summary>第三方仓库可编辑名称，官方与本地来源名称只读。</summary>
    [ObservableProperty] private string _repositoryName = string.Empty;
    /// <summary>当前远端地址；官方仅自定义渠道允许编辑。</summary>
    [ObservableProperty] private string _remoteUrl = string.Empty;
    /// <summary>第三方拉取分支，官方固定 release。</summary>
    [ObservableProperty] private string _branch = "release";
    /// <summary>与 ScriptConfig 同步的官方渠道名称。</summary>
    [ObservableProperty] private string _selectedOfficialChannel = "CNB";
    /// <summary>当前是否在加载、保存、拉取或重置。</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>向用户展示的配置状态与操作结果。</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;
    /// <summary>拉取进度，收到对象计数后切换为确定进度。</summary>
    [ObservableProperty] private int _progressValue;
    /// <summary>还没有收到可计算的 Git 进度。</summary>
    [ObservableProperty] private bool _isProgressIndeterminate;
    /// <summary>当前 Git 阶段文本。</summary>
    [ObservableProperty] private string _progressText = string.Empty;
    /// <summary>Windows 凭据中的用户名。</summary>
    [ObservableProperty] private string _gitUsername = string.Empty;
    /// <summary>只驻留窗口内存中的 Git 令牌，显式保存到 Windows 凭据管理器。</summary>
    [ObservableProperty] private string _gitToken = string.Empty;

    /// <summary>官方界面保留现有三种渠道，第三方不展示多远端选择器。</summary>
    public string[] OfficialChannels { get; } = ["CNB", "GitHub", "自定义"];
    /// <summary>官方身份与当前是否在新建第三方记录有关。</summary>
    public bool IsOfficial => !IsAddingRemote && SelectedRepository?.IsOfficial == true;
    /// <summary>第三方编辑区域在新建或选中远程来源时展示。</summary>
    public bool IsThirdParty => IsAddingRemote || SelectedRepository is { Kind: "remote", IsOfficial: false };
    /// <summary>本地仓库只展示目录操作，不展示 Git 功能。</summary>
    public bool IsLocal => !IsAddingRemote && SelectedRepository is { IsRemote: false };
    /// <summary>Git 区域仅面向托管来源。</summary>
    public bool IsRemote => IsAddingRemote || SelectedRepository?.IsRemote == true;
    /// <summary>拉取期间不能切换来源或编辑提交配置。</summary>
    public bool CanEdit => !IsBusy && !_closed;
    /// <summary>官方地址中只有自定义镜像可编辑。</summary>
    public bool CanEditRemoteUrl => CanEdit && (IsThirdParty || IsOfficial && SelectedOfficialChannel == "自定义");
    /// <summary>当前来源的固定位置。</summary>
    public string DirectoryText => IsAddingRemote ? "保存后按仓库 ID 分配固定目录" : SelectedRepository?.Directory ?? string.Empty;
    /// <summary>远程来源展示本机仓库副本，本地来源展示用户维护目录。</summary>
    public string DirectoryLabel => IsRemote ? "本机仓库副本" : "本地仓库目录";
    /// <summary>用户看到的来源类型说明。</summary>
    public string RepositoryKindText => IsOfficial ? "官方仓库 · 不可移除" : IsLocal ? "本地仓库 · 由用户维护" : "第三方远程仓库";
    /// <summary>远程来源统一使用同步语义，首次操作也只是建立本机仓库副本。</summary>
    public string UpdateButtonText => "同步仓库";
    /// <summary>关闭后返回稳定身份，让添加任务页面刷新和定位来源。</summary>
    public string? SelectedRepositoryId => SelectedRepository?.Id;

    /// <summary>使用添加页面的资源服务建立管理状态，并订阅官方渠道的共享配置。</summary>
    public PuloniaRepositoryManagerViewModel(PuloniaRepositoryManagementService service, string? repositoryId)
    {
        _service = service;
        _initialRepositoryId = repositoryId;
        _service.OfficialConfig.PropertyChanged += OnOfficialConfigChanged;
        ApplyOfficialConfig();
        try
        {
            var credential = CredentialManagerHelper.ReadCredential(CredentialName);
            GitUsername = credential?.UserName ?? string.Empty;
            GitToken = credential?.Password ?? string.Empty;
        }
        catch (Exception ex) { StatusMessage = "无法读取 Git 认证，可使用公开仓库或重新填写认证：" + ex.Message; }
    }

    /// <summary>窗口显示后读取注册记录，不下载或展开任何仓库内容。</summary>
    public Task InitializeAsync() => RunAsync(async ct => await ReloadAsync(_initialRepositoryId, ct));

    /// <summary>替换列表并恢复同一身份，移除后的选择回到官方。</summary>
    private async Task ReloadAsync(string? selectedId, CancellationToken ct)
    {
        var repositories = await _service.Repositories.GetRepositoriesAsync(ct);
        ct.ThrowIfCancellationRequested();
        Repositories = new ObservableCollection<ScriptRepositoryRegistration>(repositories);
        RepositoryChoices = new ObservableCollection<PuloniaRepositoryChoiceViewModel>(
            PuloniaRepositoryChoiceViewModel.CreateChoices(repositories, _service.OfficialConfig.SelectedChannelName));
        IsAddingRemote = false;
        SelectedRepository = Repositories.FirstOrDefault(r => r.Id == selectedId) ?? Repositories.FirstOrDefault(r => r.IsOfficial);
        FillRepository();
        if (!Directory.Exists(SelectedRepository?.Directory)) StatusMessage = "尚未同步，请点击同步仓库。";
    }

    /// <summary>切换来源时显示已保存的配置，第三方隐藏的其他远端不会被覆盖。</summary>
    partial void OnSelectedRepositoryChanged(ScriptRepositoryRegistration? value)
    {
        IsAddingRemote = false;
        FillRepository();
    }

    /// <summary>填充当前表单，配置赋值不能被当成用户编辑官方镜像。</summary>
    private void FillRepository()
    {
        _applyingConfig = true;
        try
        {
            RepositoryName = SelectedRepository?.Name ?? string.Empty;
            RemoteUrl = SelectedRepository is { } repository ? _service.GetRemoteUrl(repository) : string.Empty;
            Branch = SelectedRepository?.Branch ?? "release";
        }
        finally { _applyingConfig = false; }
        NotifyState();
        if (!IsBusy) StatusMessage = SelectedRepository is not { } current ? string.Empty
            : !Directory.Exists(current.Directory) ? "尚未同步，请点击同步仓库。"
            : current.IsRemote ? "本机仓库副本可用；同步仓库不会修改任务计划使用的版本。"
            : "本地仓库可用；刷新目录不会修改任务计划使用的版本。";
    }

    /// <summary>接收旧官方窗口的渠道变化，始终在窗口所属 UI 线程处理绑定。</summary>
    private void OnOfficialConfigChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed || args.PropertyName is not ("SelectedChannelName" or "CustomRepoUrl")) return;
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) ApplyOfficialConfig();
        else dispatcher.BeginInvoke(ApplyOfficialConfig);
    }

    /// <summary>共享配置是官方渠道唯一来源，不另行保存官方当前远端。</summary>
    private void ApplyOfficialConfig()
    {
        if (_closed || _applyingConfig) return;
        _applyingConfig = true;
        try
        {
            var channel = _service.OfficialConfig.SelectedChannelName;
            SelectedOfficialChannel = OfficialChannels.Contains(channel) ? channel : "CNB";
            if (IsOfficial) RemoteUrl = _service.GetRemoteUrl(SelectedRepository!);
            // 渠道刷新保持同一展示对象，因此不会清空选择或覆盖第三方尚未保存的表单。
            foreach (var choice in RepositoryChoices) choice.UpdateOfficialChannel(SelectedOfficialChannel);
        }
        finally { _applyingConfig = false; }
        NotifyState();
    }

    /// <summary>在新窗口选择官方渠道，同步旧窗口使用的共享配置。</summary>
    partial void OnSelectedOfficialChannelChanged(string value)
    {
        if (_applyingConfig) return;
        _service.OfficialConfig.SelectedChannelName = value;
        ApplyOfficialConfig();
    }

    /// <summary>自定义官方镜像实时沿用旧窗口的配置保存语义。</summary>
    partial void OnRemoteUrlChanged(string value)
    {
        if (!_applyingConfig && IsOfficial && SelectedOfficialChannel == "自定义")
            _service.OfficialConfig.CustomRepoUrl = value;
    }

    /// <summary>忙碌和来源类型改变后刷新绑定与命令可执行状态。</summary>
    partial void OnIsBusyChanged(bool value) => NotifyState();
    /// <summary>新增表单改变后显示第三方地址和分支。</summary>
    partial void OnIsAddingRemoteChanged(bool value) => NotifyState();

    /// <summary>公共编辑动作的可执行条件。</summary>
    private bool CanManage() => CanEdit;
    /// <summary>保存仅用于第三方配置。</summary>
    private bool CanSave() => CanEdit && IsThirdParty;
    /// <summary>下载与更新只能操作已经保存的托管仓库。</summary>
    private bool CanUpdate() => CanEdit && !IsAddingRemote && SelectedRepository?.IsRemote == true;
    /// <summary>重置必须已经存在 clone 副本。</summary>
    private bool CanReset() => CanUpdate() && Directory.Exists(SelectedRepository!.Directory);
    /// <summary>刷新目录只用于已经存在的本地仓库。</summary>
    private bool CanRefreshLocal() => CanEdit && IsLocal && Directory.Exists(SelectedRepository!.Directory);
    /// <summary>官方移除按钮不可执行，服务层还会再次验证身份。</summary>
    private bool CanRemove() => CanEdit && !IsAddingRemote && SelectedRepository is { IsOfficial: false };
    /// <summary>打开目录只能定位已经存在的副本。</summary>
    private bool CanOpenDirectory() => CanEdit && !IsAddingRemote && SelectedRepository is { } repository && Directory.Exists(repository.Directory);

    /// <summary>在同一个小窗口填写新远程来源，不立即开始网络下载。</summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    private void AddRemote()
    {
        IsAddingRemote = true;
        RepositoryName = "第三方脚本仓库";
        RemoteUrl = string.Empty;
        Branch = "release";
        StatusMessage = "填写名称、地址和分支，保存后可下载。";
    }

    /// <summary>选择本地根目录验证索引后建立引用，不加入程序更新目标。</summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task AddLocalAsync()
    {
        var dialog = new Wpf.Ui.Violeta.Win32.OpenFolderDialog
        { Description = "选择包含 repo.json 索引的本地仓库根目录", UseDescriptionForTitle = true };
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(new WindowInteropHelper(owner).Handle);
        if (accepted != true) return;
        await RunAsync(async ct =>
        {
            var repository = await _service.Repositories.AddRepositoryAsync(dialog.SelectedPath, ct);
            await ReloadAsync(repository.Id, ct);
            StatusMessage = "已添加本地引用。";
        });
    }

    /// <summary>保存第三方当前远端和分支，新增配置不会修改官方渠道。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunAsync(async ct =>
    {
        var repository = IsAddingRemote
            ? await _service.Repositories.AddRemoteRepositoryAsync(RepositoryName, RemoteUrl, Branch, ct)
            : await _service.SaveRemoteAsync(SelectedRepository!.Id, RepositoryName, RemoteUrl, Branch, ct);
        await ReloadAsync(repository.Id, ct);
        StatusMessage = "配置已保存，可以同步仓库。";
    });

    /// <summary>拉取前保存第三方编辑，并通过 UI 上下文展示 Git 阶段进度。</summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private Task UpdateAsync() => RunAsync(async ct =>
    {
        var repository = SelectedRepository!;
        if (IsThirdParty) repository = await _service.SaveRemoteAsync(repository.Id, RepositoryName, RemoteUrl, Branch, ct);
        StatusMessage = "正在同步仓库；任务计划使用的版本不会改变…";
        var progress = new Progress<(string Text, int Done, int Total)>(state =>
        {
            if (_closed || ct.IsCancellationRequested) return;
            ProgressText = state.Text;
            IsProgressIndeterminate = state.Total <= 0;
            ProgressValue = state.Total > 0 ? (int)Math.Clamp(100d * state.Done / state.Total, 0, 100) : 0;
        });
        var updated = await _service.UpdateAsync(repository.Id,
            (text, done, total) => ((IProgress<(string, int, int)>)progress).Report((text, done, total)), ct);
        await ReloadAsync(repository.Id, ct);
        StatusMessage = updated ? "同步完成；需要在任务计划中更新任务资源后才会采用新内容。" : "已经是远端最新版本；任务使用版本未改变。";
    });

    /// <summary>重新读取本地仓库目录并通知任务检查，不访问远端。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshLocal))]
    private Task RefreshLocalAsync() => RunAsync(async ct =>
    {
        await _service.RefreshLocalAsync(SelectedRepository!.Id, ct);
        StatusMessage = "本地仓库已刷新；需要在任务计划中更新任务资源后才会采用变化。";
    });

    /// <summary>确认后清理托管副本，注册和已经确认的内容保留。</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAsync()
    {
        var repository = SelectedRepository!;
        var result = await ThemedMessageBox.QuestionAsync("重置将清理当前本机仓库副本，之后需要重新同步。任务使用的旧内容会保留。",
            "重置仓库", MessageBoxButton.YesNo);
        if (result != MessageBoxResult.Yes || _closed) return;
        await RunAsync(async ct =>
        {
            await _service.ResetAsync(repository.Id, ct);
            await ReloadAsync(repository.Id, ct);
            StatusMessage = "仓库已重置，请重新同步。";
        });
    }

    /// <summary>移除引用而不删除来源文件，当前选择随后回到官方。</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task RemoveAsync() => RunAsync(async ct =>
    {
        await _service.Repositories.RemoveRepositoryAsync(SelectedRepository!.Id, ct);
        await ReloadAsync(ScriptRepositoryStore.OfficialRepositoryId, ct);
        StatusMessage = "已移除引用，本地文件与已确认任务内容仍保留。";
    });

    /// <summary>在系统文件管理器查看本地目录。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private void OpenDirectory() => Process.Start(new ProcessStartInfo(SelectedRepository!.Directory) { UseShellExecute = true });

    /// <summary>认证仅写入现有 Windows 凭据项，两个窗口继续共用。</summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    private void SaveCredentials()
    {
        try
        {
            CredentialManagerHelper.SaveCredential(CredentialName, GitUsername, GitToken,
                "Git credentials for BetterGenshinImpact script repository", CredentialPersistence.LocalMachine);
            StatusMessage = "Git 认证已保存到 Windows 凭据管理器。";
        }
        catch (Exception ex) { StatusMessage = "保存认证失败：" + ex.Message; }
    }

    /// <summary>统一处理取消、错误和忙碌状态，后台操作结束后释放取消源。</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _closed) return;
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressText = string.Empty;
        ProgressValue = 0;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed)
            {
                StatusMessage = ex.Message;
                await ThemedMessageBox.ErrorAsync(ex.Message, "仓库操作失败");
            }
        }
        finally
        {
            _operationCancellation = null;
            cancellation.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>派生展示属性和命令随选择、编辑模式、操作状态一起刷新。</summary>
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(IsOfficial), nameof(IsThirdParty), nameof(IsLocal), nameof(IsRemote), nameof(CanEdit),
                     nameof(CanEditRemoteUrl), nameof(DirectoryText), nameof(DirectoryLabel), nameof(RepositoryKindText), nameof(UpdateButtonText) })
            OnPropertyChanged(property);
        AddRemoteCommand.NotifyCanExecuteChanged(); AddLocalCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged(); ResetCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        RefreshLocalCommand.NotifyCanExecuteChanged(); OpenDirectoryCommand.NotifyCanExecuteChanged(); SaveCredentialsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>关闭后停止进度和配置监听，等待中的原生拉取通过取消令牌结束。</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _service.OfficialConfig.PropertyChanged -= OnOfficialConfigChanged;
        _operationCancellation?.Cancel();
        GitToken = string.Empty;
    }
}
