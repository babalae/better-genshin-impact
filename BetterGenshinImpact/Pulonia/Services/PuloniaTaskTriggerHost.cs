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
public sealed class PuloniaTaskTriggerHost(PuloniaTaskStore store, IPuloniaTaskService tasks,
    InstanceService instances,
    ILogger<PuloniaTaskTriggerHost> logger, TimeProvider clock, IConfigService? configService = null) : BackgroundService
{
    /// <summary>检查计划文件修改标记、重试失败热键注册的周期。</summary>
    private static readonly TimeSpan PlanPollInterval = TimeSpan.FromSeconds(30);
    /// <summary>UI 线程拥有的统一热键模型，复用应用的全局注册、键鼠监听与聊天屏蔽。</summary>
    private readonly List<HotKeySettingModel> _hotkeys = [];
    /// <summary>已注册热键的配置版本。</summary>
    private string _hotkeyRevision = "";
    /// <summary>热键冲突或注册错误。</summary>
    private string _hotkeyError = "";
    /// <summary>
    /// 调度生命周期：宿主停止或界面开始关闭时取消。这是唯一的关闭守卫，取消后不再调度、注册热键或提交热键请求。
    /// 不释放：界面关闭回调可能晚于宿主释放，仍需安全取消。
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>启动时捕获的所属 UI Dispatcher；关闭回调不能再访问可能已变成 null 的 Application.Current。</summary>
    private readonly Dispatcher? _dispatcher = Application.Current?.Dispatcher;
    /// <summary>可供界面读取的最后一次触发器反馈，不暴露服务内部游标。</summary>
    public IReadOnlyList<PuloniaTaskTriggerStatus> Statuses { get; private set; } = [];
    /// <summary>调度与平台入口的可见状态。</summary>
    public string Status { get; private set; } = "调度器尚未启动。";
    /// <summary>平台状态或游标变化通知；订阅者负责切换 UI 线程。</summary>
    public event EventHandler? Changed;

    /// <summary>只在主实例调度；读取失败时停本次自动动作而不是继续使用过期配置。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!instances.Context.IsRoot)
        {
            Status = "自动触发仅在主实例中生效，子实例不会重复调度。";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            Status = "界面正在关闭，自动触发未启动。";
            return;
        }
        // 关闭守卫只在这里挂接：界面开始关闭时由 ShutdownStarted 在所属线程释放热键并取消生命周期。
        // 先挂接再检查，避免挂接前已开始关闭而错过回调。
        dispatcher.ShutdownStarted += OnDispatcherShutdown;
        if (dispatcher.HasShutdownStarted)
        {
            _lifetime.Cancel();
            Status = "界面正在关闭，自动触发未启动。";
            return;
        }
        using var stopping = stoppingToken.Register(_lifetime.Cancel);
        var token = _lifetime.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        IReadOnlyList<PuloniaTaskPlan> plans = [];
        // 本次读档中损坏、被跳过的计划：调度检查保留它们的游标，状态栏提示用户修复。
        IReadOnlyList<string> unreadablePlanIds = [];
        var planLoadError = "";
        var version = -1L;
        var planStamp = "";
        var lastPoll = DateTimeOffset.MinValue;
        try
        {
            do
            {
                try
                {
                    var now = clock.GetUtcNow();
                    var reload = version != store.PlanChangeVersion;
                    if (!reload && now - lastPoll >= PlanPollInterval)
                    {
                        // 子实例保存的计划不改变本进程的版本号：定期比较计划文件修改标记，变化才重读。
                        lastPoll = now;
                        // 上次有计划文件读取失败（可能只是被其他程序暂时独占）时，按这一周期重试，不必等到文件再被修改。
                        reload = store.GetPlanFilesStamp() != planStamp || planLoadError.Length > 0;
                        // 注册冲突可能已由用户在快捷键页解除，热键注册失败时随这一周期重试。
                        if (!reload && _hotkeyError.Length > 0)
                            await RefreshHotkeysAsync(dispatcher, plans, token).ConfigureAwait(false);
                    }
                    if (reload)
                    {
                        // 先捕获版本和修改标记，读档期间的新保存由下一轮再次加载，不能被误标已读取。
                        version = store.PlanChangeVersion;
                        planStamp = store.GetPlanFilesStamp();
                        // 一份坏计划只跳过它自己，其余计划照常调度；修复后最迟在下一个检查周期重新读取。
                        var listed = await store.ListPlansWithFailuresAsync(token).ConfigureAwait(false);
                        plans = listed.Items;
                        unreadablePlanIds = listed.Failures.Select(item => item.Id).OfType<string>().ToArray();
                        var loadError = listed.Failures.Count == 0
                            ? ""
                            : $" 有 {listed.Failures.Count} 份计划文件无法读取，已跳过其触发器：" + string.Join("；", listed.Failures);
                        // 周期重试时只在失败内容变化后记日志，避免同一坏文件反复刷屏。
                        if (loadError != planLoadError)
                            foreach (var failure in listed.Failures)
                                logger.LogWarning("Pulonia 调度跳过无法读取的计划文件 {Path}：{Message}", failure.Path, failure.Message);
                        planLoadError = loadError;
                        lastPoll = now;
                        await RefreshHotkeysAsync(dispatcher, plans, token).ConfigureAwait(false);
                    }
                    await tasks.CheckTriggersAsync(plans, unreadablePlanIds, token).ConfigureAwait(false);
                    Statuses = tasks.GetTriggerStatuses();
                    Status = "主实例调度运行中（仅在程序运行时生效）。" + planLoadError + _hotkeyError;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Status = "调度检查失败，未继续自动触发：" + ex.Message;
                    logger.LogError(ex, "Pulonia 调度检查失败");
                    // 下一轮重新读档，不能用读档失败之前的缓存触发。
                    version = -1;
                }
                Changed?.Invoke(this, EventArgs.Empty);
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 宿主停止或界面关闭；热键由 ShutdownStarted 在所属线程释放，不向可能已退出的 Dispatcher 排队。
        }
    }

    /// <summary>在消息线程停止前释放 NativeWindow，避免宿主关闭等候已退出的 STA。</summary>
    private void OnDispatcherShutdown(object? sender, EventArgs e) => ReleaseHotkeys();

    /// <summary>
    /// 在所属 UI 线程停止调度并释放全局热键与共享键鼠监听注册；可重复调用。
    /// 热键注册回调同在 UI 线程，取消之后不会再重建注册。
    /// </summary>
    private void ReleaseHotkeys()
    {
        try { _lifetime.Cancel(); }
        finally
        {
            if (_dispatcher is { } dispatcher)
                dispatcher.ShutdownStarted -= OnDispatcherShutdown;
            foreach (var hook in _hotkeys)
            {
                try { hook.UnRegisterHotKey(); }
                catch (Exception ex) { logger.LogError(ex, "Pulonia 关闭时释放热键失败"); }
            }
            _hotkeys.Clear();
        }
    }

    /// <summary>配置版本变化才重建注册；冲突可见，不偷偷替换应用已有快捷键。</summary>
    private async Task RefreshHotkeysAsync(Dispatcher dispatcher, IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct)
    {
        var bindings = plans.SelectMany(plan => plan.Triggers.Where(item => item.Enabled
            && item.Kind == PuloniaTaskTriggerKind.Hotkey).Select(trigger => (plan, trigger))).ToArray();
        var revision = string.Join("|", bindings.Select(item
            => item.plan.Id + "/" + item.trigger.Id + "/" + item.trigger.ActivatedAtUtc.UtcTicks));
        // 注册冲突可能已由用户在快捷键页解除，定期检查时允许重试失败配置。
        if (revision == _hotkeyRevision && _hotkeyError.Length == 0) return;
        await dispatcher.InvokeAsync(() =>
        {
            // 与 ShutdownStarted 回调同在 UI 线程：关闭已开始时不再重建注册。
            if (_lifetime.IsCancellationRequested)
                return;
            foreach (var hook in _hotkeys) hook.UnRegisterHotKey();
            _hotkeys.Clear();
            _hotkeyError = "";
            foreach (var (plan, trigger) in bindings)
            {
                HotKeySettingModel? hotkeyModel = null;
                try
                {
                    // 计划校验不检查键位组合，注册前由热键入口自己确认键位可用。
                    PuloniaTaskHotkeyValidator.Validate(trigger);
                    if (HasApplicationHotkeyConflict(trigger))
                        throw new InvalidOperationException("与软件已有快捷键冲突，请选择其他键。");
                    // 不自行安装新钩子；监听模式随现有 MouseKeyMonitor 的启停与游戏前台规则生效。
                    hotkeyModel = new HotKeySettingModel(trigger.DisplayText, "Pulonia/" + plan.Id + "/" + trigger.Id,
                        trigger.Hotkey, trigger.HotkeyType.ToString(), (_, _) =>
                        {
                            if (!_lifetime.IsCancellationRequested)
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
                    _hotkeyError += $" 热键“{trigger.DisplayText}”注册失败（可能冲突）：{ex.Message}";
                }
            }
            _hotkeyRevision = revision;
        }, DispatcherPriority.Normal, ct);
    }

    /// <summary>
    /// 已有软件配置优先；即使快捷键页面尚未初始化，也不能让计划抢先占用停止键等功能。
    /// 应用热键由 HotKeyPageViewModel 构造时注册，只在首页或快捷键页创建时发生；命令行 PuloniaRun 启动只打开历史页，
    /// 此时计划热键会先注册，共享注册表按先到先得会让随后的应用热键注册失败，所以这里按配置预先检查。
    /// </summary>
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
        try { await tasks.FireHotkeyAsync(planId, triggerId, _lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 热键触发失败"); Status = "热键触发失败：" + ex.Message; }
    }

    /// <summary>命令行和单实例 IPC 共用入口，不经旧任务页、不主动启动游戏截图器。</summary>
    public async Task HandleActivationAsync(CommandLineOptions options)
    {
        if (!instances.Context.IsRoot) return;
        try
        {
            if (options.Action == CommandLineAction.PuloniaRun && options.PuloniaPlanId is { } planId)
                await tasks.EnqueueAsync(new PuloniaTaskRequest { PlanId = planId, Source = "cli" }, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { logger.LogError(ex, "Pulonia 启动入口失败"); Status = "启动请求失败：" + ex.Message; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
