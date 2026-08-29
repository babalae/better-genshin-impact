using System.ComponentModel;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpGameLifecycleTools(
    McpApplicationServices application,
    McpCommandCatalog commandCatalog)
{
    [McpServerTool(Name = "bgi_start_game", Destructive = true, OpenWorld = true), Description("启动、打开或运行原神进程。使用 genshinStartConfig.installPath 和 genshinStartArgs；仅控制游戏进程，不自动启动截图器。若游戏已运行则可恢复并激活窗口。")]
    public async Task<object> StartGame(
        [Description("必须明确设为 true，表示用户当前要求启动原神。")]
        bool confirm,
        [Description("游戏已运行或启动成功后是否恢复并前置窗口。")]
        bool activateWindow = true,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("启动原神需要将 confirm 设为 true。");
        var existing = SystemControl.FindGenshinImpactHandle();
        if (existing != IntPtr.Zero)
        {
            if (activateWindow) SystemControl.RestoreWindow(existing);
            return new { started = true, alreadyRunning = true, handle = existing.ToInt64(), activated = activateWindow };
        }

        var config = application.Services.GetRequiredService<IConfigService>().Get().GenshinStartConfig;
        if (string.IsNullOrWhiteSpace(config.InstallPath))
            throw new InvalidOperationException("genshinStartConfig.installPath 为空，请先配置原神安装路径。 ");
        var handle = await SystemControl.StartFromLocalAsync(config.InstallPath).WaitAsync(cancellationToken);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("启动命令已执行，但在等待时间内没有找到原神窗口。 ");
        TaskContext.Instance().LinkedStartGenshinTime = DateTime.Now;
        if (activateWindow) SystemControl.RestoreWindow(handle);
        return new { started = true, alreadyRunning = false, handle = handle.ToInt64(), activated = activateWindow };
    }

    [McpServerTool(Name = "bgi_activate_game_window", Destructive = true, Idempotent = true), Description("恢复、激活并前置已经运行的原神窗口；不会启动游戏或截图器。")]
    public static object ActivateGameWindow(
        [Description("必须明确设为 true，因为会改变当前前台窗口。")]
        bool confirm)
    {
        if (!confirm) throw new InvalidOperationException("激活原神窗口需要将 confirm 设为 true。");
        var handle = SystemControl.FindGenshinImpactHandle();
        if (handle == IntPtr.Zero) return new { activated = false, reason = "未找到原神窗口。" };
        SystemControl.RestoreWindow(handle);
        SystemControl.ActivateWindow(handle);
        return new { activated = true, handle = handle.ToInt64() };
    }

    [McpServerTool(Name = "bgi_minimize_game_window", Destructive = true, Idempotent = true), Description("最小化已经运行的原神窗口；不会关闭游戏、截图器或当前任务。")]
    public static object MinimizeGameWindow(
        [Description("必须明确设为 true，因为会改变游戏窗口状态。")]
        bool confirm)
    {
        if (!confirm) throw new InvalidOperationException("最小化原神窗口需要将 confirm 设为 true。");
        var handle = SystemControl.FindGenshinImpactHandle();
        if (handle == IntPtr.Zero) return new { minimized = false, reason = "未找到原神窗口。" };
        var minimized = SystemControl.MinimizeGameWindow();
        return new { minimized, handle = handle.ToInt64() };
    }

    [McpServerTool(Name = "bgi_get_game_readiness", ReadOnly = true, Idempotent = true), Description("检查运行自动化前的完整准备状态：原神窗口、关联启动配置、截图器、TaskContext、当前游戏界面和独立任务锁。AI 在启动脚本或游戏任务前应先调用本工具。")]
    public async Task<object> GetGameReadiness(CancellationToken cancellationToken = default)
    {
        var home = application.Services.GetRequiredService<HomePageViewModel>();
        var dispatcherEnabled = await Application.Current.Dispatcher.InvokeAsync(() => home.TaskDispatcherEnabled).Task;
        return BuildReadiness(dispatcherEnabled, cancellationToken);
    }

    [McpServerTool(Name = "bgi_prepare_game", Destructive = true, OpenWorld = true), Description("执行 BetterGI 标准游戏准备流程：按关联启动设置查找/启动原神，启动截图器和实时触发器，等待自动进门/界面可识别，并可从普通菜单返回主界面。不会启动具体脚本。")]
    public async Task<object> PrepareGame(
        [Description("必须明确设为 true，因为可能启动原神、激活窗口并发送返回主界面的按键。")]
        bool confirm,
        [Description("是否要求最终位于游戏主界面。false 时主界面、可关闭界面或秘境均视为已准备。")]
        bool requireMainUi = true,
        [Description("在已识别的对话/大地图/普通菜单中，是否尝试返回主界面。")]
        bool returnToMainUi = true,
        [Description("最长等待秒数，范围 10-600。")]
        int timeoutSeconds = 180,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("准备游戏需要将 confirm 设为 true。");
        if (timeoutSeconds is < 10 or > 600) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        if (TaskControl.TaskSemaphore.CurrentCount == 0)
            return new { ready = true, alreadyRunningTask = true, message = "已有独立任务正在运行，不重复执行游戏准备。", status = await GetGameReadiness(cancellationToken) };

        var home = application.Services.GetRequiredService<HomePageViewModel>();
        var steps = new List<string>();
        var dispatcherEnabled = await Application.Current.Dispatcher.InvokeAsync(() => home.TaskDispatcherEnabled).Task;
        if (!dispatcherEnabled)
        {
            steps.Add("查找原神窗口；如果启用关联启动，则按配置启动原神");
            await Application.Current.Dispatcher.InvokeAsync(home.OnStartTriggerAsync).Task.Unwrap().WaitAsync(cancellationToken);
            dispatcherEnabled = await Application.Current.Dispatcher.InvokeAsync(() => home.TaskDispatcherEnabled).Task;
            if (!dispatcherEnabled && SystemControl.FindGenshinImpactHandle() == IntPtr.Zero)
            {
                var startConfig = application.Services.GetRequiredService<IConfigService>().Get().GenshinStartConfig;
                return new
                {
                    ready = false,
                    steps,
                    reason = startConfig.LinkedStartEnabled
                        ? "关联启动未能获得原神窗口，请检查安装路径或启动状态。"
                        : "未找到原神窗口且关联启动未启用。请先启动原神，或配置 genshinStartConfig.linkedStartEnabled/installPath。",
                    status = BuildReadiness(false, cancellationToken),
                };
            }
            steps.Add("启动截图器、实时触发器和游戏遮罩");
        }
        else
        {
            steps.Add("复用已经运行的截图器和实时触发器");
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var attemptedReturn = false;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = DetectGameUi(cancellationToken);
            var ready = TaskContext.Instance().IsInitialized
                        && (requireMainUi ? state.Category == GameUiCategory.Main : state.AutomationReady);
            if (ready)
            {
                steps.Add(requireMainUi ? "确认已进入游戏主界面" : "确认游戏界面已可供自动化使用");
                return new { ready = true, steps, elapsed = timeoutSeconds - (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds), ui = state };
            }

            if (returnToMainUi && !attemptedReturn && TaskContext.Instance().IsInitialized
                && state.Category is GameUiCategory.Talk or GameUiCategory.BigMap)
            {
                attemptedReturn = true;
                steps.Add($"从 {state.Category} 界面尝试返回主界面");
                await new ReturnMainUiTask().Start(cancellationToken);
            }
            await Task.Delay(500, cancellationToken);
        }

        return new
        {
            ready = false,
            timedOut = true,
            timeoutSeconds,
            steps,
            status = BuildReadiness(dispatcherEnabled, cancellationToken),
            suggestion = "检查原神是否停留在登录、公告、更新或需要人工确认的界面；处理后再次调用 bgi_prepare_game。",
        };
    }

    [McpServerTool(Name = "bgi_return_to_main_ui", Destructive = true, Idempotent = true), Description("在截图器已启动且没有独立任务运行时，使用 BetterGI ReturnMainUiTask 从普通游戏菜单/对话逐步返回主界面。")]
    public async Task<object> ReturnToMainUi(
        [Description("必须明确设为 true，因为会向游戏发送 ESC 和界面点击。")]
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("返回主界面需要将 confirm 设为 true。");
        if (!TaskContext.Instance().IsInitialized) throw new InvalidOperationException("截图器尚未启动，请先调用 bgi_prepare_game。");
        if (TaskControl.TaskSemaphore.CurrentCount == 0) throw new InvalidOperationException("独立任务正在运行，不能并行发送返回主界面的输入。");
        await new ReturnMainUiTask().Start(cancellationToken);
        var state = DetectGameUi(cancellationToken);
        return new { completed = state.Category == GameUiCategory.Main, ui = state };
    }

    [McpServerTool(Name = "bgi_close_game", Destructive = true, Idempotent = true), Description("关闭、退出或终止当前 Windows 会话中的原神进程。先请求正常关闭，5 秒未退出时沿用 BetterGI SystemControl.CloseGame 强制终止；可先停止正在运行的自动化任务。")]
    public async Task<object> CloseGame(
        [Description("必须明确设为 true，表示用户当前要求关闭原神。")]
        bool confirm,
        [Description("存在独立任务时是否先发送手动停止并等待释放任务锁。默认 true。")]
        bool stopCurrentTask = true,
        [Description("是否停止 BetterGI 截图器和实时触发器。默认 true。")]
        bool stopCapture = true,
        [Description("等待任务停止和游戏进程消失的最长秒数，范围 1-60。")]
        int waitSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("关闭原神需要将 confirm 设为 true。");
        if (waitSeconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        var taskWasRunning = TaskControl.TaskSemaphore.CurrentCount == 0;
        if (taskWasRunning)
        {
            if (!stopCurrentTask)
                throw new InvalidOperationException("当前有自动化任务运行；请允许 stopCurrentTask，或先手动停止任务。 ");
            RunnerContext.Instance.IsSuspend = false;
            CancellationContext.Instance.ManualCancel();
            var taskDeadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (TaskControl.TaskSemaphore.CurrentCount == 0 && DateTime.UtcNow < taskDeadline)
                await Task.Delay(200, cancellationToken);
        }

        if (stopCapture)
        {
            var home = application.Services.GetRequiredService<HomePageViewModel>();
            var dispatcherEnabled = await Application.Current.Dispatcher.InvokeAsync(() => home.TaskDispatcherEnabled).Task;
            if (dispatcherEnabled)
                _ = await commandCatalog.InvokeAsync("home_page.stop_trigger", null, true, cancellationToken);
        }

        var handleBefore = SystemControl.FindGenshinImpactHandle();
        if (handleBefore == IntPtr.Zero)
            return new { closed = true, alreadyClosed = true, taskWasRunning };
        await Task.Run(SystemControl.CloseGame, CancellationToken.None).WaitAsync(cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (SystemControl.FindGenshinImpactHandle() != IntPtr.Zero && DateTime.UtcNow < deadline)
            await Task.Delay(200, cancellationToken);
        var closed = SystemControl.FindGenshinImpactHandle() == IntPtr.Zero;
        return new { closed, alreadyClosed = false, taskWasRunning, taskStopped = TaskControl.TaskSemaphore.CurrentCount != 0, captureStopped = stopCapture };
    }

    [McpServerTool(Name = "bgi_restart_game", Destructive = true, OpenWorld = true), Description("完整重启原神：停止当前自动化和截图器，关闭旧游戏进程，使用配置安装路径重新启动；可重新启动截图器并等待主界面就绪。")]
    public async Task<object> RestartGame(
        [Description("必须明确设为 true，表示用户当前要求重启原神。")]
        bool confirm,
        [Description("重启后是否启动截图器、实时触发器并等待可自动化界面。默认 true。")]
        bool prepareAutomation = true,
        [Description("prepareAutomation=true 时是否要求进入主界面。")]
        bool requireMainUi = true,
        [Description("关闭和重新准备的等待秒数，范围 30-600。")]
        int timeoutSeconds = 240,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("重启原神需要将 confirm 设为 true。");
        if (timeoutSeconds is < 30 or > 600) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        _ = await CloseGame(true, true, true, Math.Min(60, timeoutSeconds), cancellationToken);
        var startResult = await StartGame(true, activateWindow: true, cancellationToken: cancellationToken);
        if (!prepareAutomation)
            return new { restarted = true, prepared = false, start = startResult };
        var prepareResult = await PrepareGame(
            true,
            requireMainUi,
            returnToMainUi: true,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);
        return new { restarted = true, prepared = true, start = startResult, readiness = prepareResult };
    }

    private object BuildReadiness(bool dispatcherEnabled, CancellationToken cancellationToken)
    {
        var config = application.Services.GetRequiredService<IConfigService>().Get();
        var taskContext = TaskContext.Instance();
        var handle = SystemControl.FindGenshinImpactHandle();
        var ui = DetectGameUi(cancellationToken);
        return new
        {
            gameWindowFound = handle != IntPtr.Zero,
            gameWindowHandle = handle.ToInt64(),
            linkedStartEnabled = config.GenshinStartConfig.LinkedStartEnabled,
            installPathConfigured = !string.IsNullOrWhiteSpace(config.GenshinStartConfig.InstallPath),
            autoEnterGameEnabled = config.GenshinStartConfig.AutoEnterGameEnabled,
            captureDispatcherEnabled = dispatcherEnabled,
            taskContextInitialized = taskContext.IsInitialized,
            ui,
            independentTaskRunning = TaskControl.TaskSemaphore.CurrentCount == 0,
            readyForMainUiTask = taskContext.IsInitialized && ui.Category == GameUiCategory.Main,
            readyForGeneralAutomation = taskContext.IsInitialized && ui.AutomationReady,
        };
    }

    private static GameUiState DetectGameUi(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TaskContext.Instance().IsInitialized) return new GameUiState(GameUiCategory.Unknown, false, false, "截图器未初始化");
        try
        {
            using var region = TaskControl.CaptureToRectArea();
            var category = Bv.WhichGameUi(region);
            var inDomain = Bv.IsInDomain(region);
            var closable = Bv.IsInAnyClosableUi(region);
            return new GameUiState(category, inDomain, category == GameUiCategory.Main || inDomain || closable,
                category != GameUiCategory.Unknown ? category.ToString() : inDomain ? "Domain" : closable ? "ClosableUi" : "Unknown");
        }
        catch (Exception ex)
        {
            return new GameUiState(GameUiCategory.Unknown, false, false, $"截图识别失败：{ex.GetBaseException().Message}");
        }
    }

    private sealed record GameUiState(GameUiCategory Category, bool InDomain, bool AutomationReady, string Description);
}
