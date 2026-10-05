using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>创建弹窗的仓库切换与管理，只为当前选择加载资源。</summary>
public partial class PuloniaTaskCreationDialogViewModel
{
    /// <summary>正在替换仓库列表，绑定导致的选中变化不能重复启动索引加载。</summary>
    private bool _isUpdatingRepositories;

    /// <summary>弹窗已关闭，后台请求不得再次填充资源或提交创建结果。</summary>
    private bool _isClosed;

    /// <summary>当前索引请求，关闭弹窗时取消等待来源锁和后台读取。</summary>
    private CancellationTokenSource? _resourceLoadCancellationTokenSource;

    /// <summary>添加仓库完成后优先选择的稳定身份。</summary>
    private string? _repositoryToSelect;

    /// <summary>官方渠道的共享配置，仅在资源选择器初始化后监听，关闭时解除订阅。</summary>
    private ScriptConfig? _repositoryDisplayConfig;

    /// <summary>管理窗口打开状态仅阻止父窗口操作，不表示正在读取或提取任务资源。</summary>
    [ObservableProperty]
    private bool _isRepositoryManagerOpen;

    /// <summary>顶部选择器的仓库列表，仅包含未移除的来源。</summary>
    [ObservableProperty]
    private ObservableCollection<ScriptRepositoryRegistration> _repositories = [];

    /// <summary>包装原始注册对象的展示项，提供短来源与类型，不改变任务资源身份。</summary>
    [ObservableProperty]
    private ObservableCollection<PuloniaRepositoryChoiceViewModel> _repositoryChoices = [];

    /// <summary>当前浏览的仓库，不影响任何已有任务的来源。</summary>
    [ObservableProperty]
    private ScriptRepositoryRegistration? _selectedRepository;

    /// <summary>地图追踪与 JS 显示仓库管理，其他任务仍沿用各自资源入口。</summary>
    public bool SupportsRepositories => SelectedDefinition?.TaskType is "javascript" or "pathing";

    /// <summary>读取索引、添加来源或确认任务期间禁用仓库管理，避免混用选择。</summary>
    public bool CanManageRepositories => SupportsRepositories && !IsBusy && !IsRepositoryManagerOpen && !_isClosed;

    /// <summary>未下载、读取失败或没有资源时引导用户在独立窗口管理当前来源。</summary>
    public bool ShowRepositoryGuidance => SupportsRepositories && !IsBusy && _allResources.Count == 0;

    /// <summary>空状态对应的来源管理说明，预览不会自动触发下载。</summary>
    public string RepositoryGuidance => SelectedRepository is { IsRemote: true } repository && !Directory.Exists(repository.Directory)
        ? "当前仓库尚未下载，请打开仓库管理下载资源。" : "当前仓库没有可选资源，可打开仓库管理更新或添加来源。";

    /// <summary>显示当前本地位置，帮助区分同名仓库和开发目录。</summary>
    public string SelectedRepositoryDirectory => SelectedRepository?.Directory ?? "请选择或添加仓库";

    /// <summary>刷新列表并恢复当前或上次选择；只有目录信息，不读取其他仓库的索引。</summary>
    private async Task ReloadRepositoriesAsync(CancellationToken ct)
    {
        var preferredId = _repositoryToSelect ?? SelectedRepository?.Id
            ?? await _resourceCatalog.Repositories.GetSelectedRepositoryIdAsync(SelectedDefinition!.TaskType, ct);
        var repositories = await _resourceCatalog.Repositories.GetRepositoriesAsync(ct);
        ct.ThrowIfCancellationRequested();
        _isUpdatingRepositories = true;
        try
        {
            Repositories = new ObservableCollection<ScriptRepositoryRegistration>(repositories);
            if (_repositoryDisplayConfig is null)
            {
                _repositoryDisplayConfig = TaskContext.Instance().Config.ScriptConfig;
                _repositoryDisplayConfig.PropertyChanged += OnRepositoryDisplayConfigChanged;
            }
            RepositoryChoices = new ObservableCollection<PuloniaRepositoryChoiceViewModel>(
                PuloniaRepositoryChoiceViewModel.CreateChoices(repositories, _repositoryDisplayConfig.SelectedChannelName));
            SelectedRepository = Repositories.FirstOrDefault(r => r.Id == preferredId)
                ?? Repositories.FirstOrDefault(r => string.Equals(r.Directory, ScriptRepoUpdater.CenterRepoPath,
                    StringComparison.OrdinalIgnoreCase))
                ?? Repositories.FirstOrDefault();
            _repositoryToSelect = null;
        }
        finally { _isUpdatingRepositories = false; }
    }

    /// <summary>官方渠道变化只刷新现有展示项，不重建列表、重新选中或读取仓库资源。</summary>
    private void OnRepositoryDisplayConfigChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_isClosed || args.PropertyName != nameof(ScriptConfig.SelectedChannelName)) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) RefreshRepositoryDisplayChannel();
        else dispatcher.BeginInvoke(RefreshRepositoryDisplayChannel);
    }

    /// <summary>通过 UI 线程传播官方渠道摘要，已关闭的窗口不再处理排队通知。</summary>
    private void RefreshRepositoryDisplayChannel()
    {
        if (_isClosed) return;
        foreach (var choice in RepositoryChoices) choice.UpdateOfficialChannel(_repositoryDisplayConfig?.SelectedChannelName);
    }

    /// <summary>切换来源时先清空旧树和预览，再只读取当前来源的索引。</summary>
    partial void OnSelectedRepositoryChanged(ScriptRepositoryRegistration? value)
    {
        OnPropertyChanged(nameof(SelectedRepositoryDirectory));
        NotifyRepositoryCommands();
        if (_isUpdatingRepositories || _isClosed) return;
        ClearResourceSelection();
        _ = InitializeAsync();
    }

    /// <summary>清除旧来源的资源和参数预览，禁止加载失败时继续选择旧资源。</summary>
    private void ClearResourceSelection()
    {
        _filterCancellationTokenSource?.Cancel();
        _allResources = [];
        FilteredResources = [];
        FilteredResourceTree = [];
        SelectedResourceTreeNode = null;
        SelectedResource = null;
    }

    /// <summary>仓库操作仅在资源选择空闲时可执行。</summary>
    private bool CanAddRepository() => CanManageRepositories;

    /// <summary>移除需要明确选中的仓库；加载和确认时不得变更来源。</summary>
    private bool CanRemoveRepository() => CanManageRepositories && SelectedRepository is { IsOfficial: false };

    /// <summary>选择已有本地来源的入口统一引导至独立仓库管理窗口。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRepository))]
    private Task AddLocalRepositoryAsync() => OpenRepositoryManagerAsync();

    /// <summary>官方及第三方的远端配置和拉取统一在独立管理窗口进行。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRepository))]
    private Task AddRemoteRepositoryAsync() => OpenRepositoryManagerAsync();

    /// <summary>来源移除统一进入管理窗口；已有任务保留身份和确认内容。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveRepository))]
    private Task RemoveRepositoryAsync() => OpenRepositoryManagerAsync();

    /// <summary>打开当前选择的仓库管理，关闭后刷新列表并定位所选来源。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRepository))]
    private async Task OpenRepositoryManagerAsync()
    {
        // 停止已失去焦点的预览，让管理窗口更新仓库时及时取得来源锁。
        IsRepositoryManagerOpen = true;
        _previewCancellationTokenSource?.Cancel();
        ++_previewGeneration;
        IsResourceDetailsLoading = false;
        try
        {
            _repositoryToSelect = BetterGenshinImpact.View.Windows.Pulonia.PuloniaRepositoryManagerWindow.Show(
                _resourceCatalog.Repositories, SelectedRepository?.Id, GetRepositoryDialogOwner());
        }
        catch (Exception ex) when (IsRepositoryOperationError(ex))
        {
            await ThemedMessageBox.ErrorAsync("无法打开仓库管理：" + ex.Message);
        }
        finally { IsRepositoryManagerOpen = false; }
        if (!_isClosed) await InitializeAsync(forceRefresh: true);
    }

    /// <summary>管理窗口的模态状态独立于资源加载，刷新父窗口按钮与进度展示。</summary>
    partial void OnIsRepositoryManagerOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoading));
        NotifyRepositoryCommands();
    }

    /// <summary>刷新仓库选择器与命令可执行状态。</summary>
    private void NotifyRepositoryCommands()
    {
        OnPropertyChanged(nameof(CanManageRepositories));
        AddLocalRepositoryCommand.NotifyCanExecuteChanged();
        AddRemoteRepositoryCommand.NotifyCanExecuteChanged();
        RemoveRepositoryCommand.NotifyCanExecuteChanged();
        OpenRepositoryManagerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowRepositoryGuidance));
        OnPropertyChanged(nameof(RepositoryGuidance));
    }

    /// <summary>收敛可以向用户报告的仓库输入、磁盘和拉取错误。</summary>
    private static bool IsRepositoryOperationError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or LibGit2Sharp.LibGit2SharpException or Newtonsoft.Json.JsonException or System.Net.Http.HttpRequestException;

    /// <summary>输入与目录对话框跟随当前活动窗口，避免显示在创建弹窗后面。</summary>
    private static Window? GetRepositoryDialogOwner()
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    /// <summary>关闭任何入口都取消资源请求，任务确认前的后台读取不得在关闭后提交。</summary>
    public void Close()
    {
        if (_isClosed) return;
        _isClosed = true;
        if (_repositoryDisplayConfig is not null)
            _repositoryDisplayConfig.PropertyChanged -= OnRepositoryDisplayConfigChanged;
        ++_previewGeneration;
        _resourceLoadCancellationTokenSource?.Cancel();
        _previewCancellationTokenSource?.Cancel();
        _filterCancellationTokenSource?.Cancel();
        NotifyRepositoryCommands();
    }
}
