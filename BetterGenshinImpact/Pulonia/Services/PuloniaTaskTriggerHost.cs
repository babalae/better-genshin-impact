using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>主实例的唯一异步调度循环，负责程序内定时、两种快捷键与手动命令行入口。</summary>
public sealed class PuloniaTaskTriggerHost(PuloniaTaskStore store, PuloniaTaskService tasks,
    InstanceService instances,
    ILogger<PuloniaTaskTriggerHost> logger, TimeProvider clock, IConfigService? configService = null) : BackgroundService
{
    /// <summary>UI 线程拥有的统一热键模型，复用应用的全局注册、键鼠监听与聊天屏蔽。</summary>
    private readonly List<HotKeySettingModel> _hotkeys = [];
    /// <summary>已注册热键的配置签名。</summary>
    private string _hotkeySignature = "";
    /// <summary>热键冲突或注册错误。</summary>
    private string _hotkeyError = "";
    /// <summary>应用启动或关闭命令取消信号。</summary>
    private CancellationToken _stopToken;
    /// <summary>启动时捕获的所属 UI Dispatcher；关闭回调不能再访问可能已变成 null 的 Application.Current。</summary>
    private readonly Dispatcher? _dispatcher = Application.Current?.Dispatcher;
    /// <summary>UI 线程上的终止性热键清理标记，防止停止后晚到的刷新重新创建 NativeWindow。</summary>
    private volatile bool _hotkeysReleased;
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
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            Status = "界面正在关闭，自动触发未启动。";
            return;
        }
        try { await dispatcher.InvokeAsync(() =>
        {
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                dispatcher.ShutdownStarted += OnDispatcherShutdown;
        }, DispatcherPriority.Normal, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested
            || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { return; }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 热键清理事件绑定失败"); }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        IReadOnlyList<PuloniaTaskPlan> plans = [];
        var version = -1L;
        var lastLoad = DateTimeOffset.MinValue;
        try
        {
            do
            {
                // Dispatcher 已开始关闭时停止调度，不等待宿主取消信号再提交新请求。
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    break;
                try
                {
                    var now = clock.GetUtcNow();
                    if (version != store.PlanChangeVersion || now - lastLoad >= TimeSpan.FromSeconds(30))
                    {
                        // 先捕获版本，读档期间的新保存由下一轮再次加载，不能被误标已读取。
                        version = store.PlanChangeVersion;
                        plans = await store.ListPlansAsync(stoppingToken).ConfigureAwait(false);
                        lastLoad = now;
                        await RefreshHotkeysAsync(plans, stoppingToken).ConfigureAwait(false);
                    }
                    await tasks.CheckTriggersAsync(plans, stoppingToken).ConfigureAwait(false);
                    States = await tasks.ListTriggerStatesAsync(stoppingToken).ConfigureAwait(false);
                    Status = "主实例调度运行中（仅在程序运行时生效）。" + _hotkeyError;
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested
            || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
        finally
        {
            // WPF 关闭时 ShutdownStarted 已在所属线程释放钩子，不向已退出的 Dispatcher 排队。
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                try { await dispatcher.InvokeAsync(ReleaseHotkeys); }
                catch (OperationCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    // 排队后恰好关闭时，由所属线程的 ShutdownStarted 回调完成清理。
                }
            }
        }
    }

    /// <summary>在消息线程停止前释放 NativeWindow，避免宿主关闭等候已退出的 STA。</summary>
    private void OnDispatcherShutdown(object? sender, EventArgs e) => ReleaseHotkeys();

    /// <summary>在所属 UI 线程释放全局热键与共享键鼠监听注册。</summary>
    private void ReleaseHotkeys()
    {
        if (_hotkeysReleased)
            return;
        _hotkeysReleased = true;
        if (_dispatcher is { } dispatcher)
            dispatcher.ShutdownStarted -= OnDispatcherShutdown;
        foreach (var hook in _hotkeys)
        {
            try { hook.UnRegisterHotKey(); }
            catch (Exception ex) { logger.LogError(ex, "Pulonia 关闭时释放热键失败"); }
        }
        _hotkeys.Clear();
    }

    /// <summary>配置签名变化才重建注册；冲突可见，不偷偷替换应用已有快捷键。</summary>
    private async Task RefreshHotkeysAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;
        var bindings = plans.SelectMany(plan => plan.Triggers.Where(item => item.Enabled
            && item.Kind == PuloniaTaskTriggerKind.Hotkey).Select(trigger => (plan, trigger))).ToArray();
        var signature = string.Join("|", bindings.Select(item => item.plan.Id + PuloniaTaskSchedule.Signature(item.trigger)));
        // 注册冲突可能已由用户在快捷键页解除，定期读档时允许重试失败配置。
        if (signature == _hotkeySignature && _hotkeyError.Length == 0) return;
        await dispatcher.InvokeAsync(() =>
        {
            if (_hotkeysReleased || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            foreach (var hook in _hotkeys) hook.UnRegisterHotKey();
            _hotkeys.Clear();
            _hotkeyError = "";
            foreach (var (plan, trigger) in bindings)
            {
                HotKeySettingModel? hotkeyModel = null;
                try
                {
                    PuloniaTaskSchedule.Validate(trigger);
                    if (HasApplicationHotkeyConflict(trigger))
                        throw new InvalidOperationException("与软件已有快捷键冲突，请选择其他键。");
                    // 不自行安装新钩子；监听模式随现有 MouseKeyMonitor 的启停与游戏前台规则生效。
                    hotkeyModel = new HotKeySettingModel(trigger.Name, "Pulonia/" + plan.Id + "/" + trigger.Id,
                        trigger.Hotkey, trigger.HotkeyType.ToString(), (_, _) =>
                        {
                            if (!_stopToken.IsCancellationRequested && !_hotkeysReleased
                                && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                                _ = FireHotkeySafelyAsync(plan.Id, trigger.Id);
                        }) { SuppressRepeat = true };
                    hotkeyModel.RegisterHotKey();
                    if (hotkeyModel.HotKey.IsEmpty)
                        throw new InvalidOperationException(hotkeyModel.RegistrationError ?? "快捷键注册失败。");
                    _hotkeys.Add(hotkeyModel);
                }
                catch (Exception ex)
                {
                    hotkeyModel?.UnRegisterHotKey();
                    _hotkeyError += $" 热键“{trigger.Name}”注册失败（可能冲突）：{ex.Message}";
                }
            }
            _hotkeySignature = signature;
        }, DispatcherPriority.Normal, ct);
    }

    /// <summary>已有软件配置优先；即使快捷键页面尚未初始化，也不能让计划抢先占用停止键等功能。</summary>
    private bool HasApplicationHotkeyConflict(PuloniaTaskTrigger trigger)
    {
        var config = configService?.Get().HotKeyConfig;
        if (config is null) return false;
        var key = HotKey.FromString(trigger.Hotkey);
        foreach (var property in config.GetType().GetProperties().Where(property => property.PropertyType == typeof(string)
            && property.Name.EndsWith("Hotkey", StringComparison.Ordinal)))
        {
            if (!Enum.TryParse<HotKeyTypeEnum>(config.GetType().GetProperty(property.Name + "Type")?.GetValue(config) as string, out var type))
                continue;
            try
            {
                if (HotKeySettingModel.AreConflicting(key, trigger.HotkeyType,
                    HotKey.FromString(property.GetValue(config) as string ?? ""), type)) return true;
            }
            catch (ArgumentException)
            {
                // 原设置中的非法键值不是一个可注册热键，不因它阻断其他有效配置。
            }
        }
        return false;
    }

    /// <summary>观察热键异步异常，避免 async void 异常穿过系统消息循环。</summary>
    private async Task FireHotkeySafelyAsync(string planId, string triggerId)
    {
        try { await tasks.FireHotkeyAsync(planId, triggerId, _stopToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 热键触发失败"); Status = "热键触发失败：" + ex.Message; }
    }

    /// <summary>命令行和单实例 IPC 共用入口，不经旧任务页、不主动启动游戏截图器。</summary>
    public async Task HandleActivationAsync(CommandLineOptions options)
    {
        if (!instances.Context.IsRoot) return;
        try
        {
            if (options.Action == CommandLineAction.PuloniaRun && options.PuloniaPlanId is { } planId)
                await tasks.EnqueueAsync(new PuloniaTaskRequest { PlanId = planId, Source = "cli" }, _stopToken).ConfigureAwait(false);
        }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 启动入口失败"); Status = "启动请求失败：" + ex.Message; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
