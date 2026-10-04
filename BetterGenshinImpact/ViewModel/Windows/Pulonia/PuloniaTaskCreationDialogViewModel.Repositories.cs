using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Repositories;
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

    /// <summary>顶部选择器的仓库列表，仅包含未移除的来源。</summary>
    [ObservableProperty]
    private ObservableCollection<ScriptRepositoryRegistration> _repositories = [];

    /// <summary>当前浏览的仓库，不影响任何已有任务的来源。</summary>
    [ObservableProperty]
    private ScriptRepositoryRegistration? _selectedRepository;

    /// <summary>地图追踪与 JS 显示仓库管理，其他任务仍沿用各自资源入口。</summary>
    public bool SupportsRepositories => SelectedDefinition?.TaskType is "javascript" or "pathing";

    /// <summary>读取索引、添加来源或确认任务期间禁用仓库管理，避免混用选择。</summary>
    public bool CanManageRepositories => SupportsRepositories && !IsBusy && !_isClosed;

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
            SelectedRepository = Repositories.FirstOrDefault(r => r.Id == preferredId)
                ?? Repositories.FirstOrDefault(r => string.Equals(r.Directory, ScriptRepoUpdater.CenterRepoPath,
                    StringComparison.OrdinalIgnoreCase))
                ?? Repositories.FirstOrDefault();
            _repositoryToSelect = null;
        }
        finally { _isUpdatingRepositories = false; }
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
    private bool CanRemoveRepository() => CanManageRepositories && SelectedRepository is not null;

    /// <summary>选择已有本地拉取目录，注册成功后直接切换到该来源。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRepository))]
    private async Task AddLocalRepositoryAsync()
    {
        var dialog = new Wpf.Ui.Violeta.Win32.OpenFolderDialog
        {
            Description = "选择已拉取的脚本仓库根目录（包含 repo.json 索引）",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(SelectedRepository?.Directory) ? SelectedRepository!.Directory : string.Empty
        };
        var owner = GetRepositoryDialogOwner();
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(new WindowInteropHelper(owner).Handle);
        if (accepted != true) return;
        IsBusy = true;
        try
        {
            var registration = await _resourceCatalog.Repositories.AddRepositoryAsync(dialog.SelectedPath);
            _repositoryToSelect = registration.Id;
        }
        catch (Exception ex) when (IsRepositoryOperationError(ex))
        {
            await ThemedMessageBox.ErrorAsync("添加本地仓库失败：" + ex.Message);
        }
        finally { IsBusy = false; }
        if (_repositoryToSelect is not null && !_isClosed) await InitializeAsync(forceRefresh: true);
    }

    /// <summary>复用现有 Git 拉取入口添加官方或第三方来源，不修改订阅渠道。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRepository))]
    private async Task AddRemoteRepositoryAsync()
    {
        var dialog = new PromptDialog("输入脚本仓库 Git 地址，可以使用官方或第三方仓库。", "添加远程仓库",
            new TextBox(), ScriptRepoUpdater.RepoChannels["CNB"])
        { Owner = GetRepositoryDialogOwner() };
        if (dialog.ShowDialog() != true) return;
        var url = dialog.ResponseText.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "ssh"))
        {
            await ThemedMessageBox.ErrorAsync("请输入有效的 Git 仓库地址。支持 HTTPS、HTTP 或 SSH 地址。");
            return;
        }
        IsBusy = true;
        StatusMessage = "正在拉取仓库，请稍候…";
        try
        {
            // 拉取器协调自己的更新锁；拉取结束后才验证和注册，不重复持有来源锁。
            var (directory, _) = await Task.Run(() => ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(url, null));
            var registration = await _resourceCatalog.Repositories.AddRepositoryAsync(directory);
            _repositoryToSelect = registration.Id;
        }
        catch (Exception ex) when (IsRepositoryOperationError(ex))
        {
            StatusMessage = "添加远程仓库失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage);
        }
        finally { IsBusy = false; }
        if (_repositoryToSelect is not null && !_isClosed) await InitializeAsync(forceRefresh: true);
    }

    /// <summary>移除当前浏览来源；已有任务保留身份和内容，再次添加同目录会恢复该身份。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveRepository))]
    private async Task RemoveRepositoryAsync()
    {
        var repository = SelectedRepository!;
        IsBusy = true;
        try
        {
            await _resourceCatalog.Repositories.RemoveRepositoryAsync(repository.Id);
            _isUpdatingRepositories = true;
            try { SelectedRepository = null; }
            finally { _isUpdatingRepositories = false; }
            ClearResourceSelection();
        }
        catch (Exception ex) when (IsRepositoryOperationError(ex))
        {
            await ThemedMessageBox.ErrorAsync("移除仓库失败：" + ex.Message);
        }
        finally { IsBusy = false; }
        if (!_isClosed) await InitializeAsync();
    }

    /// <summary>刷新仓库选择器与命令可执行状态。</summary>
    private void NotifyRepositoryCommands()
    {
        OnPropertyChanged(nameof(CanManageRepositories));
        AddLocalRepositoryCommand.NotifyCanExecuteChanged();
        AddRemoteRepositoryCommand.NotifyCanExecuteChanged();
        RemoveRepositoryCommand.NotifyCanExecuteChanged();
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
        ++_previewGeneration;
        _resourceLoadCancellationTokenSource?.Cancel();
        _previewCancellationTokenSource?.Cancel();
        _filterCancellationTokenSource?.Cancel();
        NotifyRepositoryCommands();
    }
}
