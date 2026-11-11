using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Service.KeyMacro;

/// <summary>
/// 按键宏的热键宿主：按宏目录和总开关注册热键，持有每个宏的 Runner，向界面提供运行状态和注册错误。
/// 热键的注册与注销都在 UI 线程上进行
/// </summary>
public sealed class KeyMacroHotkeyHost : IHostedService
{
    /// <summary>
    /// 设置变化后重建注册的防抖时间
    /// </summary>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(200);

    private readonly KeyMacroStore _store;

    /// <summary>
    /// 配置根：读取总开关和应用快捷键配置
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 运行环境服务，判断游戏前台时读取当前窗口
    /// </summary>
    private readonly GameRuntimeService _runtimeService;

    /// <summary>
    /// 停止信号：「停止当前脚本/独立任务」热键、截图器停止、程序退出时停止全部按键宏
    /// </summary>
    private readonly TaskStopService _taskStopService;

    private readonly ILogger<KeyMacroHotkeyHost> _logger;

    /// <summary>
    /// 所属 UI 线程
    /// </summary>
    private readonly Dispatcher? _dispatcher = Application.Current?.Dispatcher;

    /// <summary>
    /// 保护 <see cref="_entries"/>：停止信号可能来自其他线程
    /// </summary>
    private readonly object _entriesLock = new();

    /// <summary>
    /// 每个宏的注册状态
    /// </summary>
    private readonly Dictionary<KeyMacroDefinition, Entry> _entries = [];

    /// <summary>
    /// 重建注册的防抖计时器
    /// </summary>
    private DispatcherTimer? _refreshTimer;

    /// <summary>
    /// 停止信号的注册句柄
    /// </summary>
    private IDisposable? _stopRegistration;

    /// <summary>
    /// 是否已初始化
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// 界面是否已开始关闭，此后不再注册热键
    /// </summary>
    private bool _shutdown;

    /// <summary>
    /// 注册状态或运行状态变化，在 UI 线程上触发
    /// </summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// 当前实例是否响应快捷键。云原神网页版实例不注册任何热键，规则与应用热键相同
    /// </summary>
    public bool IsHotkeyAvailable { get; } = !InstanceBootstrap.Current.Context.IsWebView;

    /// <summary>
    /// 创建热键宿主
    /// </summary>
    public KeyMacroHotkeyHost(KeyMacroStore store, IConfigService configService, GameRuntimeService runtimeService,
        TaskStopService taskStopService, ILogger<KeyMacroHotkeyHost> logger)
    {
        _store = store;
        _config = configService.Get();
        _runtimeService = runtimeService;
        _taskStopService = taskStopService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _dispatcher?.InvokeAsync(Initialize);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopAll("程序退出");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 宏是否正在运行
    /// </summary>
    public bool IsRunning(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) && entry.Runner?.IsRunning == true;
        }
    }

    /// <summary>
    /// 宏的快捷键是否已注册
    /// </summary>
    public bool IsRegistered(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) && entry.Model != null;
        }
    }

    /// <summary>
    /// 宏的快捷键注册失败原因，没有错误时返回 null
    /// </summary>
    public string? GetError(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) ? entry.Error : null;
        }
    }

    /// <summary>
    /// 停止全部正在运行的按键宏，可在任意线程调用
    /// </summary>
    /// <param name="reason">停止原因，写入日志</param>
    public void StopAll(string reason)
    {
        List<KeyMacroRunner> runners;
        lock (_entriesLock)
        {
            runners = _entries.Values.Select(e => e.Runner).OfType<KeyMacroRunner>().ToList();
        }

        foreach (var runner in runners)
        {
            runner.Stop(reason);
        }
    }

    /// <summary>
    /// 在 UI 线程上载入宏目录、订阅变化并注册热键
    /// </summary>
    private void Initialize()
    {
        if (_initialized || _dispatcher == null || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _initialized = true;
        _store.EnsureLoaded();
        _store.Macros.CollectionChanged += OnMacrosChanged;
        foreach (var definition in _store.Macros)
        {
            definition.PropertyChanged += OnDefinitionPropertyChanged;
        }

        _config.KeyMacroConfig.PropertyChanged += OnConfigPropertyChanged;
        _stopRegistration = _taskStopService.Register(reason => StopAll(reason switch
        {
            TaskStopReason.RuntimeStopped => "截图器停止",
            TaskStopReason.ApplicationShutdown => "程序退出",
            _ => "停止任务"
        }));
        _dispatcher.ShutdownStarted += OnShutdownStarted;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        Refresh();
    }

    /// <summary>
    /// 按当前设置同步注册：移除已删除的宏，签名变化的宏重新注册
    /// </summary>
    private void Refresh()
    {
        if (_shutdown)
        {
            return;
        }

        var definitions = _store.Macros.ToList();
        lock (_entriesLock)
        {
            foreach (var stale in _entries.Keys.Except(definitions).ToList())
            {
                _entries[stale].Dispose("宏已删除");
                _entries.Remove(stale);
            }

            foreach (var definition in definitions)
            {
                var signature = GetSignature(definition);
                if (_entries.TryGetValue(definition, out var existing))
                {
                    if (existing.Signature == signature)
                    {
                        continue;
                    }

                    existing.Dispose("设置已变化");
                }

                _entries[definition] = CreateEntry(definition, signature);
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 影响注册的设置组成的签名，签名不变时保留现有注册和正在运行的宏
    /// </summary>
    private string GetSignature(KeyMacroDefinition definition)
    {
        return string.Join("|", _config.KeyMacroConfig.Enabled, definition.Name, definition.Enabled, definition.Hotkey,
            definition.EffectiveHotkeyType, definition.TriggerMode);
    }

    /// <summary>
    /// 为一个宏创建 Runner 并注册热键。不满足条件时只记录原因
    /// </summary>
    private Entry CreateEntry(KeyMacroDefinition definition, string signature)
    {
        var entry = new Entry(signature);
        if (!_config.KeyMacroConfig.Enabled || !definition.Enabled || string.IsNullOrEmpty(definition.Hotkey))
        {
            return entry;
        }

        if (!IsHotkeyAvailable)
        {
            entry.Error = "云原神网页版实例不响应快捷键";
            return entry;
        }

        HotKey hotKey;
        try
        {
            hotKey = HotKey.FromString(definition.Hotkey);
        }
        catch (ArgumentException)
        {
            entry.Error = "快捷键格式无效";
            return entry;
        }

        if (hotKey.IsEmpty)
        {
            return entry;
        }

        var type = definition.EffectiveHotkeyType;
        if (type == HotKeyTypeEnum.KeyboardMonitor && hotKey.Modifiers != ModifierKeys.None)
        {
            entry.Error = "键鼠监听不支持组合键";
            return entry;
        }

        if (type == HotKeyTypeEnum.GlobalRegister && hotKey.MouseButton != MouseButton.Left)
        {
            entry.Error = "全局热键不支持鼠标侧键";
            return entry;
        }

        if (HotKeySettingModel.ConflictsWithHotKeyConfig(hotKey, type, _config.HotKeyConfig))
        {
            entry.Error = "与软件已有的快捷键冲突，请在快捷键页查看";
            return entry;
        }

        var runner = new KeyMacroRunner(definition, hotKey, IsGameForeground, GetGameArea);
        var configName = "KeyMacro/" + definition.Name;
        var model = type == HotKeyTypeEnum.GlobalRegister
            ? new HotKeySettingModel(definition.Name, configName, hotKey.ToString(), type.ToString(), (_, _) => runner.OnPressed())
            {
                SuppressRepeat = true
            }
            : new HotKeySettingModel(definition.Name, configName, hotKey.ToString(), type.ToString(), null,
                definition.TriggerMode == KeyMacroTriggerMode.Hold)
            {
                OnKeyDownAction = (_, _) => runner.OnKeyDown(),
                OnKeyUpAction = (_, _) => runner.OnKeyUp()
            };
        model.RegisterHotKey();
        if (model.HotKey.IsEmpty)
        {
            entry.Error = model.RegistrationError ?? "快捷键注册失败";
            model.UnRegisterHotKey();
            _logger.LogWarning("按键宏「{Name}」的快捷键 {Hotkey} 注册失败：{Error}", definition.Name, definition.Hotkey, entry.Error);
            return entry;
        }

        runner.RunningChanged += OnRunnerRunningChanged;
        entry.Model = model;
        entry.Runner = runner;
        return entry;
    }

    /// <summary>
    /// 游戏是否在前台：有运行环境时看它的窗口，没有时按前台进程名判断
    /// </summary>
    private bool IsGameForeground()
    {
        return SystemControl.IsGameForeground(_runtimeService.Current?.Window);
    }

    /// <summary>
    /// 游戏画面在屏幕上的区域，供绝对坐标指令换算：有运行环境时用它的截图区域（与 GameRegion1080PPosMove 相同）；
    /// 截图器未启动时用前台窗口的游戏画面区域，执行每条指令前已确认前台窗口是游戏
    /// </summary>
    private RECT? GetGameArea()
    {
        if (_runtimeService.Current is { } runtime)
        {
            return runtime.SystemInfo.CaptureAreaRect;
        }

        var handle = SystemControl.GetForegroundWindowHandle();
        return handle == 0 ? null : SystemControl.GetCaptureRect(handle);
    }

    /// <summary>
    /// 计划一次防抖后的重建
    /// </summary>
    private void ScheduleRefresh()
    {
        if (_refreshTimer == null || _shutdown)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>
    /// 宏列表变化：订阅新增的宏，取消订阅移除的宏
    /// </summary>
    private void OnMacrosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<KeyMacroDefinition>() ?? [])
        {
            removed.PropertyChanged -= OnDefinitionPropertyChanged;
        }

        foreach (var added in e.NewItems?.OfType<KeyMacroDefinition>() ?? [])
        {
            added.PropertyChanged -= OnDefinitionPropertyChanged;
            added.PropertyChanged += OnDefinitionPropertyChanged;
        }

        ScheduleRefresh();
    }

    /// <summary>
    /// 宏的设置变化。脚本变化不需要重新注册，Runner 在每次开始时读取最新脚本
    /// </summary>
    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KeyMacroDefinition.Script))
        {
            ScheduleRefresh();
        }
    }

    /// <summary>
    /// 总开关变化
    /// </summary>
    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyMacroConfig.Enabled))
        {
            ScheduleRefresh();
        }
    }

    /// <summary>
    /// Runner 的运行状态变化，切换到 UI 线程通知界面
    /// </summary>
    private void OnRunnerRunningChanged(object? sender, EventArgs e)
    {
        _dispatcher?.BeginInvoke(() => StatusChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// 界面开始关闭：停止全部宏，在所属线程注销热键
    /// </summary>
    private void OnShutdownStarted(object? sender, EventArgs e)
    {
        _shutdown = true;
        _refreshTimer?.Stop();
        _stopRegistration?.Dispose();
        lock (_entriesLock)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Dispose("程序退出");
            }

            _entries.Clear();
        }

        _store.Flush();
    }

    /// <summary>
    /// 一个宏的注册状态
    /// </summary>
    private sealed class Entry(string signature)
    {
        /// <summary>
        /// 创建时的设置签名
        /// </summary>
        public string Signature { get; } = signature;

        /// <summary>
        /// 已注册的热键，未注册时为 null
        /// </summary>
        public HotKeySettingModel? Model { get; set; }

        /// <summary>
        /// 宏的 Runner，未注册时为 null
        /// </summary>
        public KeyMacroRunner? Runner { get; set; }

        /// <summary>
        /// 未注册的原因，没有错误时为 null
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 停止正在运行的宏并注销热键
        /// </summary>
        public void Dispose(string reason)
        {
            Runner?.Stop(reason);
            Model?.UnRegisterHotKey();
            Model = null;
        }
    }
}
