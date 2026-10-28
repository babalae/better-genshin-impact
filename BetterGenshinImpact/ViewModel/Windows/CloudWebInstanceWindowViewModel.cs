using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 主实例中的云原神管理界面，复用现有存储、启动器和 IPC 服务管理实例生命周期。
/// </summary>
public sealed partial class CloudWebInstanceWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>实例目录与名称校验服务。</summary>
    private readonly WebViewInstanceStore _store;

    /// <summary>独立进程启动器。</summary>
    private readonly WebViewInstanceLauncher _launcher;

    /// <summary>在线连接、窗口激活与关闭服务。</summary>
    private readonly InstanceService _instanceService;

    /// <summary>独立任务名称查询服务。</summary>
    private readonly SoloTaskRegistry _registry;

    /// <summary>返回主界面后打开任务页面。</summary>
    private readonly INavigationService _navigationService;

    /// <summary>只在窗口存续期间检查实例进程与目录变化。</summary>
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>窗口关闭后取消启动等待，避免异步操作继续更新已释放的界面。</summary>
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    /// <summary>防止重复释放及后台事件访问已关闭窗口。</summary>
    private bool _disposed;

    /// <summary>按实例名复用的行集合，刷新不改变用户正在查看的列表位置。</summary>
    public ObservableCollection<CloudWebInstanceRowViewModel> Instances { get; } = [];

    /// <summary>是否有实例，供列表和首次使用提示切换。</summary>
    public bool HasInstances => Instances.Count > 0;

    /// <summary>顶部紧凑汇总，不把在线进程数量称为已就绪游戏数量。</summary>
    public string SummaryText => $"{Instances.Count} 个实例 · {Instances.Count(row => row.IsRunning)} 个运行中";

    /// <summary>在列表上方展开新建表单。</summary>
    [ObservableProperty]
    private bool _isCreating;

    /// <summary>用户输入的实例名称，具体规则仍由存储服务统一校验。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAndLaunchCommand))]
    private string _newInstanceName = string.Empty;

    /// <summary>新建表单中的校验错误。</summary>
    [ObservableProperty]
    private string? _creationError;

    /// <summary>操作失败或读取目录失败时在窗口内展示，避免轮询反复弹窗。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>当前操作提示，与顶部进度环一起展示。</summary>
    [ObservableProperty]
    private string _operationText = string.Empty;

    /// <summary>未确认的操作期间禁用修改命令，防止启动、删除与关闭相互交错。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateAndLaunchCommand))]
    private bool _isOperationPending;

    /// <summary>由窗口响应关闭请求；ViewModel 不直接操作窗口控件。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>创建管理窗口状态，订阅 IPC 变化并启动轻量进程状态刷新。</summary>
    public CloudWebInstanceWindowViewModel(
        WebViewInstanceStore store,
        WebViewInstanceLauncher launcher,
        InstanceService instanceService,
        SoloTaskRegistry registry,
        INavigationService navigationService)
    {
        _store = store;
        _launcher = launcher;
        _instanceService = instanceService;
        _registry = registry;
        _navigationService = navigationService;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnInstancesChanged;
        _instanceService.ConnectedInstancesChanged += OnInstancesChanged;
        _instanceService.SoloTaskStatusesChanged += OnInstancesChanged;
        RefreshInstances();
        _refreshTimer.Start();
    }

    /// <summary>操作开始和结束时立即同步各行权限。</summary>
    partial void OnIsOperationPendingChanged(bool value)
    {
        RefreshInstances();
    }

    /// <summary>当前是否允许编辑实例。</summary>
    private bool CanEdit() => !_disposed && !IsOperationPending;

    /// <summary>非空名称才允许提交；完整校验在提交时执行。</summary>
    private bool CanCreateAndLaunch() => CanEdit() && !string.IsNullOrWhiteSpace(NewInstanceName);

    /// <summary>展开内联新建表单，保留尚未提交的名称。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BeginCreate()
    {
        CreationError = null;
        ErrorMessage = null;
        IsCreating = true;
    }

    /// <summary>收起表单，不创建任何实例或浏览器数据。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void CancelCreate()
    {
        IsCreating = false;
        NewInstanceName = string.Empty;
        CreationError = null;
    }

    /// <summary>校验名称后创建实例并打开游戏，首次登录在独立游戏窗口完成。</summary>
    [RelayCommand(CanExecute = nameof(CanCreateAndLaunch))]
    private async Task CreateAndLaunchAsync()
    {
        CreationError = null;
        ErrorMessage = null;
        // 缺少运行时时先说明并引导安装，避免创建实例后启动一个注定失败的进程
        if (!await WebViewRuntimePrompt.EnsureAvailableAsync())
        {
            return;
        }

        string name;
        try
        {
            name = _store.Create(NewInstanceName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            CreationError = ex.Message;
            return;
        }

        IsCreating = false;
        NewInstanceName = string.Empty;
        RefreshInstances();
        var row = Instances.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            // 创建成功后即使打开失败也保留此行，用户可以直接重试，无需再次创建。
            await OpenInstanceAsync(row);
        }
    }

    /// <summary>根据该行最新状态判断是否允许打开游戏或查看窗口。</summary>
    private bool CanOpenInstance(CloudWebInstanceRowViewModel? row) => CanEdit() && row?.CanOpen == true;

    /// <summary>未运行时启动独立进程；已连接时通过 IPC 激活游戏窗口。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenInstance))]
    private async Task OpenInstanceAsync(CloudWebInstanceRowViewModel? row)
    {
        if (row is null || !CanOpenInstance(row))
        {
            return;
        }

        ErrorMessage = null;
        OperationText = $"正在打开「{row.Name}」…";
        IsOperationPending = true;
        try
        {
            if (row.Endpoint is { } endpoint)
            {
                await _instanceService.ActivateChildInstanceAsync(endpoint, _lifetimeCancellation.Token);
            }
            else
            {
                // 启动新进程前检查运行时，缺失时说明并引导安装
                if (!await WebViewRuntimePrompt.EnsureAvailableAsync())
                {
                    return;
                }

                _launcher.Launch(row.Name);
                // 新进程获取实例名互斥体需要一点时间，稍后再刷新运行状态。
                await Task.Delay(3000, _lifetimeCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // 用户关闭管理窗口仅取消界面等待，已经打开的游戏继续运行。
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or TimeoutException)
        {
            ErrorMessage = $"打开实例失败：{ex.Message}";
        }
        finally
        {
            IsOperationPending = false;
        }
    }

    /// <summary>只有已连接的实例可以通过 IPC 正常关闭。</summary>
    private bool CanCloseInstance(CloudWebInstanceRowViewModel? row) => CanEdit() && row?.CanClose == true;

    /// <summary>确认后正常关闭指定实例，保留其登录状态。</summary>
    [RelayCommand(CanExecute = nameof(CanCloseInstance))]
    private async Task CloseInstanceAsync(CloudWebInstanceRowViewModel? row)
    {
        if (row?.Endpoint is not { } endpoint || !CanCloseInstance(row))
        {
            return;
        }

        ErrorMessage = null;
        OperationText = $"正在关闭「{row.Name}」…";
        IsOperationPending = true;
        try
        {
            var result = await ThemedMessageBox.QuestionAsync(
                $"关闭实例「{row.Name}」？\n该实例正在执行的任务也会停止，登录状态会保留。",
                "关闭云原神实例");
            if (result == MessageBoxResult.Yes && !_disposed)
            {
                await _instanceService.CloseChildInstanceAsync(endpoint, _lifetimeCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // 管理窗口关闭后不再等待远端确认，由连接事件反映实例的最终状态。
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            ErrorMessage = $"关闭实例失败：{ex.Message}";
        }
        finally
        {
            IsOperationPending = false;
        }
    }

    /// <summary>实例完全退出后才允许删除。</summary>
    private bool CanDeleteInstance(CloudWebInstanceRowViewModel? row) => CanEdit() && row?.CanDelete == true;

    /// <summary>删除前明确说明登录态与网页数据会一并清除，并由存储层再次检查进程。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteInstance))]
    private async Task DeleteInstanceAsync(CloudWebInstanceRowViewModel? row)
    {
        if (row is null || !CanDeleteInstance(row))
        {
            return;
        }

        ErrorMessage = null;
        OperationText = $"正在删除「{row.Name}」…";
        IsOperationPending = true;
        try
        {
            var result = await ThemedMessageBox.QuestionAsync(
                $"确定删除实例「{row.Name}」吗？\n该实例的登录态等 WebView 数据会一并删除，且无法恢复。",
                "删除云原神网页版实例");
            if (result == MessageBoxResult.Yes && !_disposed)
            {
                _store.Delete(row.Name);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"删除实例失败：{ex.Message}";
        }
        finally
        {
            IsOperationPending = false;
        }
    }

    /// <summary>与删除相同，只有实例完全退出后才允许清理缓存，避免删除浏览器正在使用的文件。</summary>
    private bool CanClearCache(CloudWebInstanceRowViewModel? row) => CanDeleteInstance(row);

    /// <summary>清理实例的网页缓存，保留登录状态；完成后说明释放了多少空间。</summary>
    [RelayCommand(CanExecute = nameof(CanClearCache))]
    private async Task ClearCacheAsync(CloudWebInstanceRowViewModel? row)
    {
        if (row is null || !CanClearCache(row))
        {
            return;
        }

        ErrorMessage = null;
        OperationText = $"正在清理「{row.Name}」的缓存…";
        IsOperationPending = true;
        try
        {
            var result = await Task.Run(() => _store.ClearCache(row.Name));
            var skipped = result.SkippedFiles > 0 ? $"，{result.SkippedFiles} 个文件正在使用，已跳过" : string.Empty;
            Toast.Success($"已清理「{row.Name}」的缓存，释放 {ByteSizeFormatter.Format(result.FreedBytes)}{skipped}。登录状态已保留");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"清理缓存失败：{ex.Message}";
        }
        finally
        {
            IsOperationPending = false;
        }
    }

    /// <summary>先收起管理窗口，再通过现有任务页面选择执行实例和任务参数。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenTasks()
    {
        Close();
        _navigationService.Navigate(typeof(TaskSettingsPage));
    }

    /// <summary>关闭管理窗口，不停止任何已打开的游戏。</summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>实例连接、任务事件与定时检查统一切回当前窗口的 UI 线程。</summary>
    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        if (_disposed || _refreshTimer.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (_refreshTimer.Dispatcher.CheckAccess())
        {
            RefreshInstances();
        }
        else
        {
            _refreshTimer.Dispatcher.BeginInvoke(RefreshInstances);
        }
    }

    /// <summary>重新读取实例列表与运行状态，尽量保持已有展示行和列表位置。</summary>
    private void RefreshInstances()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var names = _store.List();
            var endpoints = _instanceService.GetConnectedChildInstances()
                .Where(endpoint => endpoint.InstanceType == BetterGiInstanceType.WebView)
                .ToArray();

            // 目录是实例名的唯一存储；只移除实际已删除的行，不因进程离线而移除实例。
            foreach (var row in Instances.Where(row => !names.Contains(row.Name, StringComparer.OrdinalIgnoreCase)).ToArray())
            {
                Instances.Remove(row);
            }

            for (var index = 0; index < names.Count; index++)
            {
                var name = names[index];
                var row = Instances.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new CloudWebInstanceRowViewModel(name);
                    Instances.Insert(index, row);
                }
                else if (Instances.IndexOf(row) != index)
                {
                    Instances.Move(Instances.IndexOf(row), index);
                }

                var endpoint = endpoints.FirstOrDefault(item => string.Equals(item.InstanceName, name, StringComparison.OrdinalIgnoreCase));
                var instance = new CloudWebInstanceItem(name, WebViewInstanceStore.IsRunning(name));
                var status = endpoint is null ? null : _instanceService.GetSoloTaskStatus(endpoint);
                row.Update(instance, endpoint, status, _registry.GetDisplayName, IsOperationPending);
            }

            OnPropertyChanged(nameof(HasInstances));
            OnPropertyChanged(nameof(SummaryText));
            OpenInstanceCommand.NotifyCanExecuteChanged();
            CloseInstanceCommand.NotifyCanExecuteChanged();
            DeleteInstanceCommand.NotifyCanExecuteChanged();
            ClearCacheCommand.NotifyCanExecuteChanged();
            OpenTasksCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"读取实例列表失败：{ex.Message}";
        }
    }

    /// <summary>停止轮询、解除实例事件订阅并取消尚未结束的界面等待。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnInstancesChanged;
        _instanceService.ConnectedInstancesChanged -= OnInstancesChanged;
        _instanceService.SoloTaskStatusesChanged -= OnInstancesChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
