using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// 「Worker 日志」独立窗口的生命周期管理，两侧共用：
/// <list type="bullet">
/// <item>Worker 侧：显示位置为 <see cref="WorkerLogDisplayMode.RemoteWindow"/> 时，日志直接写进本窗口；</item>
/// <item>控制侧：收到 Worker 回传的日志行（显示位置为 <see cref="WorkerLogDisplayMode.LocalWindow"/>）时写入本窗口。</item>
/// </list>
/// <para>
/// 注册为托管服务：构造时就要订阅 <see cref="WorkerController.LogReceived"/>，
/// 否则控制侧既没有别的地方解析本服务，Worker 回传的日志就没人接收。
/// 窗口必须由 UI 线程创建，因此所有入口都自行切到 Dispatcher；进程没有 UI 时静默放弃，
/// 日志本身仍然会落到文件日志里。
/// </para>
/// </summary>
public sealed class WorkerLogWindowService : IHostedService
{
    private readonly ILogger<WorkerLogWindowService> _logger;
    private WorkerLogWindowViewModel? _viewModel;
    private WorkerLogWindow? _window;
    private bool _closedByUser;

    public WorkerLogWindowService(ILogger<WorkerLogWindowService> logger, WorkerController workerController)
    {
        _logger = logger;
        // 控制侧：Worker 只在显示位置为「本地独立窗口」时回传日志，收到即显示
        workerController.LogReceived += OnWorkerLogReceived;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 追加日志行（Worker 侧入口）。用户手动关闭过窗口则不再自动弹出，避免打断正在使用的会话
    /// </summary>
    public void Append(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_closedByUser)
            {
                return;
            }

            var viewModel = EnsureWindow();
            viewModel?.Append(lines);
        });
    }

    /// <summary>
    /// 显示位置变化时调用：切到本侧负责的独立窗口时立刻把窗口弹出来，让用户看到设置已生效
    /// </summary>
    public void HandleModeChanged(WorkerLogDisplayMode mode)
    {
        // 「远程独立窗口」只由 Worker 侧打开，「本地独立窗口」只由控制侧打开，
        // 同一个进程不会两侧都开，因此这里按角色过滤
        var expected = InstanceBootstrap.Current.Context.IsHeadless
            ? WorkerLogDisplayMode.RemoteWindow
            : WorkerLogDisplayMode.LocalWindow;
        if (mode != expected)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            _closedByUser = false;
            EnsureWindow();
        });
    }

    private void OnWorkerLogReceived(object? sender, WorkerLogBatch batch)
    {
        Append(batch.Lines);
    }

    /// <summary>
    /// 仅可在 UI 线程调用
    /// </summary>
    private WorkerLogWindowViewModel? EnsureWindow()
    {
        if (_window is not null)
        {
            return _viewModel;
        }

        try
        {
            // 复用同一个 ViewModel：用户关掉窗口再切回来时历史日志还在
            _viewModel ??= new WorkerLogWindowViewModel(ResolveTitle());
            var window = new WorkerLogWindow(_viewModel)
            {
                // Worker 侧没有主界面，日志窗口会被游戏盖住，因此置顶显示；控制侧保持普通窗口
                Topmost = InstanceBootstrap.Current.Context.IsHeadless
            };
            window.Closed += OnWindowClosed;
            window.Show();
            _window = window;
            _logger.LogInformation("已打开 Worker 日志独立窗口：{Title}", _viewModel.Title);
            return _viewModel;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开 Worker 日志独立窗口失败");
            _viewModel = null;
            _window = null;
            _closedByUser = true;
            return null;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is WorkerLogWindow window)
        {
            window.Closed -= OnWindowClosed;
        }

        _window = null;
        _closedByUser = true;
    }

    private static string ResolveTitle()
    {
        return InstanceBootstrap.Current.Context.IsHeadless
            ? "Worker 日志"
            : "Worker 日志（远程）";
    }

    private void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            // 应用正在退出时 Dispatcher 已关闭
            _logger.LogDebug(exception, "投递 Worker 日志窗口操作失败");
        }
    }
}
