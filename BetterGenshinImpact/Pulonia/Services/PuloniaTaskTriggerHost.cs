using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.Instance;
using Fischless.HotkeyCapture;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>主实例的唯一异步调度循环，负责热键与 Windows 启动入口适配。</summary>
public sealed class PuloniaTaskTriggerHost(PuloniaTaskStore store, PuloniaTaskService tasks,
    PuloniaUserActivityMonitor activity, InstanceService instances, PuloniaWindowsTaskScheduler windows,
    ILogger<PuloniaTaskTriggerHost> logger, TimeProvider clock) : BackgroundService
{
    /// <summary>UI 线程拥有的全局热键注册。</summary>
    private readonly List<HotkeyHook> _hotkeys = [];
    /// <summary>已注册热键的配置签名。</summary>
    private string _hotkeySignature = "";
    /// <summary>已同步的系统任务签名，避免每秒写系统任务。</summary>
    private string _windowsSignature = "";
    /// <summary>下一次系统注册失败重试时间。</summary>
    private DateTimeOffset _windowsRetry;
    /// <summary>热键冲突或注册错误。</summary>
    private string _hotkeyError = "";
    /// <summary>系统任务同步错误。</summary>
    private string _windowsError = "";
    /// <summary>应用启动或关闭命令取消信号。</summary>
    private CancellationToken _stopToken;
    /// <summary>可供界面读取的最后一次状态，不暴露服务内部游标。</summary>
    public IReadOnlyList<PuloniaTaskTriggerState> States { get; private set; } = [];
    /// <summary>调度与平台入口的可见状态。</summary>
    public string Status { get; private set; } = "调度器尚未启动。";
    /// <summary>平台状态或游标变化通知；订阅者负责切换 UI 线程。</summary>
    public event EventHandler? Changed;

    /// <summary>只在主实例调度；读取失败时停本次自动动作而不是继续使用过期配置。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopToken = stoppingToken;
        if (!instances.Context.IsRoot)
        {
            Status = "自动触发仅在主实例中生效，子实例不会重复调度。";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        try { await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Application.Current.Dispatcher.ShutdownStarted += OnDispatcherShutdown;
            activity.Start();
        }); }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 用户活动检测启动失败"); }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        IReadOnlyList<PuloniaTaskPlan> plans = [];
        var version = -1L;
        var lastLoad = DateTimeOffset.MinValue;
        try
        {
            do
            {
                try
                {
                    var now = clock.GetUtcNow();
                    if (version != store.PlanChangeVersion || now - lastLoad >= TimeSpan.FromSeconds(30))
                    {
                        // 先捕获版本，读档期间的新保存由下一轮再次加载，不能被误标已读取。
                        version = store.PlanChangeVersion;
                        plans = await store.ListPlansAsync(stoppingToken).ConfigureAwait(false);
                        lastLoad = now;
                        await RefreshHotkeysAsync(plans).ConfigureAwait(false);
                    }
                    await tasks.CheckTriggersAsync(plans, stoppingToken).ConfigureAwait(false);
                    States = await tasks.ListTriggerStatesAsync(stoppingToken).ConfigureAwait(false);
                    await SynchronizeWindowsAsync(plans, now).ConfigureAwait(false);
                    Status = "主实例调度运行中。" + (!activity.DesktopAvailable ? "桌面不可用，自动任务等待解锁。" : "")
                        + _hotkeyError + _windowsError;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Status = "调度检查失败，未继续自动触发：" + ex.Message;
                    logger.LogError(ex, "Pulonia 调度检查失败");
                    // 下一轮重新读档，不能用读档失败之前的缓存触发。
                    version = -1;
                }
                Changed?.Invoke(this, EventArgs.Empty);
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            // WPF 关闭时 ShutdownStarted 已在所属线程释放钩子，不向已退出的 Dispatcher 排队。
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.HasShutdownStarted) await dispatcher.InvokeAsync(ReleasePlatformHooks);
            else activity.Dispose();
        }
    }

    /// <summary>在消息线程停止前释放 NativeWindow，避免宿主关闭等候已退出的 STA。</summary>
    private void OnDispatcherShutdown(object? sender, EventArgs e) => ReleasePlatformHooks();

    /// <summary>在拥有窗口的 UI 线程释放注册和活动钩子。</summary>
    private void ReleasePlatformHooks()
    {
        Application.Current.Dispatcher.ShutdownStarted -= OnDispatcherShutdown;
        foreach (var hook in _hotkeys) hook.Dispose();
        _hotkeys.Clear();
        activity.Dispose();
    }

    /// <summary>配置签名变化才重建注册；冲突可见，不偷偷替换应用已有快捷键。</summary>
    private async Task RefreshHotkeysAsync(IReadOnlyList<PuloniaTaskPlan> plans)
    {
        var bindings = plans.SelectMany(plan => plan.Triggers.Where(item => item.Enabled
            && item.Kind == PuloniaTaskTriggerKind.Hotkey).Select(trigger => (plan, trigger))).ToArray();
        var signature = string.Join("|", bindings.Select(item => item.plan.Id + PuloniaTaskSchedule.Signature(item.trigger)));
        if (signature == _hotkeySignature) return;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var hook in _hotkeys) hook.Dispose();
            _hotkeys.Clear();
            _hotkeyError = "";
            foreach (var (plan, trigger) in bindings)
            {
                var hook = new HotkeyHook();
                try
                {
                    var hotkey = HotKey.FromString(trigger.Hotkey);
                    hook.RegisterHotKey((User32.HotKeyModifiers)((int)hotkey.Modifiers | 0x4000),
                        (System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(hotkey.Key));
                    hook.KeyPressed += (_, _) => _ = FireHotkeySafelyAsync(plan.Id, trigger.Id);
                    _hotkeys.Add(hook);
                }
                catch (Exception ex)
                {
                    hook.Dispose();
                    _hotkeyError += $" 热键“{trigger.Name}”注册失败（可能冲突）：{ex.Message}";
                }
            }
            _hotkeySignature = signature;
        });
    }

    /// <summary>观察热键异步异常，避免 async void 异常穿过系统消息循环。</summary>
    private async Task FireHotkeySafelyAsync(string planId, string triggerId)
    {
        try { await tasks.FireHotkeyAsync(planId, triggerId, _stopToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 热键触发失败"); Status = "热键触发失败：" + ex.Message; }
    }

    /// <summary>系统任务只唤起下一次检查；后续 Cron 和 CD 始终由内核按原时区计算。</summary>
    private async Task SynchronizeWindowsAsync(IReadOnlyList<PuloniaTaskPlan> plans, DateTimeOffset now)
    {
        var entries = plans.SelectMany(plan => plan.Triggers.Where(item => item.Enabled && item.LaunchWithWindows)
            .Select(trigger => (trigger, next: PuloniaTaskSchedule.Next(trigger, now)))).Where(item => item.next is not null)
            .OrderBy(item => item.next).ToArray();
        var next = entries.FirstOrDefault();
        var signature = next.next?.ToString("O") + "/" + (next.trigger?.WakeDevice ?? false);
        if (signature == _windowsSignature || now < _windowsRetry) return;
        try
        {
            await Task.Run(() => windows.Synchronize(next.next, next.trigger?.WakeDevice ?? false)).ConfigureAwait(false);
            _windowsSignature = signature;
            _windowsError = "";
        }
        catch (Exception ex)
        {
            _windowsError = " Windows 唤起注册失败（程序内调度仍有效）：" + ex.Message;
            _windowsRetry = now.AddMinutes(1);
        }
    }

    /// <summary>命令行和单实例 IPC 共用入口，不经旧任务页、不主动启动游戏截图器。</summary>
    public async Task HandleActivationAsync(CommandLineOptions options)
    {
        if (!instances.Context.IsRoot) return;
        try
        {
            if (options.Action == CommandLineAction.PuloniaDispatch)
                await tasks.CheckTriggersAsync(await store.ListPlansAsync(_stopToken).ConfigureAwait(false), _stopToken).ConfigureAwait(false);
            else if (options.Action == CommandLineAction.PuloniaRun && options.PuloniaPlanId is { } planId)
                await tasks.EnqueueAsync(new PuloniaTaskRequest { PlanId = planId, Source = "cli" }, _stopToken).ConfigureAwait(false);
        }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 启动入口失败"); Status = "启动请求失败：" + ex.Message; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
