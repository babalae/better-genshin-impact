using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// 一个按键宏的触发状态机和执行线程。快捷键的设置变化时由宿主丢弃并重建，所以名称、触发方式、快捷键在实例内不变；
/// 脚本在每次开始执行时读取最新内容
/// </summary>
internal sealed class KeyMacroRunner
{
    /// <summary>
    /// 循环模式下每轮之间的最小间隔，避免没有等待的脚本以 CPU 速度发送输入
    /// </summary>
    private const int MinLoopIntervalMs = 10;

    /// <summary>
    /// 按住循环开始后多久才检查快捷键的物理状态。键盘钩子回调时系统按键状态可能尚未更新
    /// </summary>
    private const int HoldCheckGraceMs = 150;

    /// <summary>
    /// 游戏前台状态的缓存时长，避免每条指令都查询前台进程
    /// </summary>
    private const int ForegroundCacheMs = 50;

    /// <summary>
    /// 跳过无效指令的警告最多列出几处错误
    /// </summary>
    private const int MaxLoggedErrors = 3;

    private static readonly ILogger Logger = App.GetLogger<KeyMacroRunner>();

    /// <summary>
    /// 保护执行线程、取消源和停止原因
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 宏定义，开始执行时读取最新脚本
    /// </summary>
    private readonly KeyMacroDefinition _definition;

    /// <summary>
    /// 宏名称（创建时的快照，用于日志和线程名）
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 触发方式
    /// </summary>
    private readonly KeyMacroTriggerMode _mode;

    /// <summary>
    /// 解析选项，包含快捷键对应的按键
    /// </summary>
    private readonly KeyMacroParseOptions _parseOptions;

    /// <summary>
    /// 按住循环时用于检查物理按键状态的虚拟键
    /// </summary>
    private readonly User32.VK? _holdCheckKey;

    /// <summary>
    /// 判断游戏是否在前台
    /// </summary>
    private readonly Func<bool> _isGameForeground;

    /// <summary>
    /// 取游戏画面在屏幕上的区域，供 moveto 和带坐标的 click 换算坐标
    /// </summary>
    private readonly Func<RECT?> _getGameArea;

    /// <summary>
    /// 正在执行的线程，未运行时为 null
    /// </summary>
    private Thread? _thread;

    /// <summary>
    /// 本次执行的取消源
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 本次执行的停止原因，第一次设置后不再覆盖
    /// </summary>
    private string? _stopReason;

    /// <summary>
    /// 快捷键是否处于按下状态（键鼠监听类型），用于过滤长按时重复的 KeyDown
    /// </summary>
    private volatile bool _keyHeld;

    /// <summary>
    /// 上次解析的脚本文本
    /// </summary>
    private string? _cachedScript;

    /// <summary>
    /// 上次解析的结果
    /// </summary>
    private KeyMacroDocument _cachedDocument = KeyMacroDocument.Empty;

    /// <summary>
    /// 运行状态变化。在执行线程或热键线程上触发，订阅者负责切换线程
    /// </summary>
    public event EventHandler? RunningChanged;

    /// <summary>
    /// 是否正在执行
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _thread != null;
            }
        }
    }

    /// <summary>
    /// 创建 Runner
    /// </summary>
    /// <param name="definition">宏定义</param>
    /// <param name="hotKey">已注册的快捷键</param>
    /// <param name="isGameForeground">判断游戏是否在前台</param>
    /// <param name="getGameArea">取游戏画面在屏幕上的区域，取不到时返回 null</param>
    public KeyMacroRunner(KeyMacroDefinition definition, HotKey hotKey, Func<bool> isGameForeground, Func<RECT?> getGameArea)
    {
        _definition = definition;
        _name = definition.Name;
        _mode = definition.TriggerMode;
        _isGameForeground = isGameForeground;
        _getGameArea = getGameArea;
        _parseOptions = CreateParseOptions(_mode, hotKey);
        _holdCheckKey = _parseOptions.HotkeyKey ?? _parseOptions.HotkeyButton switch
        {
            KeyMacroMouseButton.X1 => User32.VK.VK_XBUTTON1,
            KeyMacroMouseButton.X2 => User32.VK.VK_XBUTTON2,
            _ => null
        };
    }

    /// <summary>
    /// 按快捷键生成解析选项：单键快捷键不能出现在脚本里。
    /// 组合键（只有全局热键可用）单独按主键不会触发，不做这项检查
    /// </summary>
    public static KeyMacroParseOptions CreateParseOptions(KeyMacroTriggerMode mode, HotKey hotKey)
    {
        if (hotKey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            return new KeyMacroParseOptions(mode, null,
                hotKey.MouseButton == MouseButton.XButton1 ? KeyMacroMouseButton.X1 : KeyMacroMouseButton.X2);
        }

        if (hotKey.Key == Key.None || hotKey.Modifiers != ModifierKeys.None)
        {
            return new KeyMacroParseOptions(mode);
        }

        return new KeyMacroParseOptions(mode, (User32.VK)KeyInterop.VirtualKeyFromKey(hotKey.Key));
    }

    /// <summary>
    /// 键鼠监听：快捷键按下
    /// </summary>
    public void OnKeyDown()
    {
        if (_keyHeld)
        {
            // 长按时键盘钩子会重复触发 KeyDown
            return;
        }

        _keyHeld = true;
        OnTrigger();
    }

    /// <summary>
    /// 键鼠监听：快捷键松开。按住循环立即终止
    /// </summary>
    public void OnKeyUp()
    {
        _keyHeld = false;
        if (_mode == KeyMacroTriggerMode.Hold)
        {
            Stop("松开快捷键");
        }
    }

    /// <summary>
    /// 全局热键：快捷键按下（全局热键收不到松开事件）
    /// </summary>
    public void OnPressed()
    {
        OnTrigger();
    }

    /// <summary>
    /// 请求停止。等待会立即结束，执行线程随后松开按下的键
    /// </summary>
    /// <param name="reason">停止原因，写入日志</param>
    public void Stop(string reason)
    {
        lock (_lock)
        {
            RequestStop(reason);
        }
    }

    /// <summary>
    /// 快捷键触发：未运行时开始执行；切换循环在运行中再次触发时停止
    /// </summary>
    private void OnTrigger()
    {
        lock (_lock)
        {
            if (_thread != null)
            {
                if (_mode == KeyMacroTriggerMode.Toggle)
                {
                    RequestStop("再次按下快捷键");
                }

                // 单次执行中再按忽略；按住循环按住期间不会再次触发
                return;
            }

            if (!_isGameForeground())
            {
                return;
            }

            var document = GetDocument(_definition.Script);
            if (document.HasError)
            {
                var errors = document.Diagnostics.Where(d => d.IsError).Take(MaxLoggedErrors).Select(d => d.DisplayText);
                Logger.LogWarning("按键宏「{Name}」有 {Count} 处错误，已跳过对应指令：{Errors}", _name, document.ErrorCount,
                    string.Join("；", errors));
            }

            if (document.Commands.Count == 0)
            {
                Logger.LogWarning("按键宏「{Name}」没有可执行的指令", _name);
                return;
            }

            _cts = new CancellationTokenSource();
            _stopReason = null;
            var commands = document.Commands;
            var token = _cts.Token;
            _thread = new Thread(() => Run(commands, token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = $"KeyMacro-{_name}"
            };
            _thread.Start();
        }

        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 在持有锁时请求停止
    /// </summary>
    private void RequestStop(string reason)
    {
        if (_cts == null || _cts.IsCancellationRequested)
        {
            return;
        }

        _stopReason ??= reason;
        _cts.Cancel();
    }

    /// <summary>
    /// 记录停止原因（只记录第一次）
    /// </summary>
    private void SetStopReason(string reason)
    {
        lock (_lock)
        {
            _stopReason ??= reason;
        }
    }

    /// <summary>
    /// 按脚本文本缓存解析结果
    /// </summary>
    private KeyMacroDocument GetDocument(string script)
    {
        if (!ReferenceEquals(script, _cachedScript) && script != _cachedScript)
        {
            _cachedDocument = KeyMacroParser.Parse(script, _parseOptions);
            _cachedScript = script;
        }

        return _cachedDocument;
    }

    /// <summary>
    /// 执行线程：按触发方式执行一遍或循环执行，结束时松开按下的键
    /// </summary>
    private void Run(IReadOnlyList<KeyMacroStep> commands, CancellationToken ct)
    {
        var executor = new KeyMacroExecutor(_getGameArea);
        var startTimestamp = Stopwatch.GetTimestamp();
        var foregroundChecked = false;
        var lastForegroundCheck = 0L;
        var lastForeground = true;
        Logger.LogInformation("按键宏「{Name}」开始执行", _name);

        // 停止条件：取消、游戏不在前台、按住循环时快捷键已松开
        bool ShouldContinue()
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            var now = Stopwatch.GetTimestamp();
            if (!foregroundChecked || Stopwatch.GetElapsedTime(lastForegroundCheck, now).TotalMilliseconds >= ForegroundCacheMs)
            {
                lastForeground = _isGameForeground();
                lastForegroundCheck = now;
                foregroundChecked = true;
            }

            if (!lastForeground)
            {
                SetStopReason("游戏不在前台");
                return false;
            }

            if (_mode != KeyMacroTriggerMode.Hold)
            {
                return true;
            }

            if (!_keyHeld)
            {
                SetStopReason("松开快捷键");
                return false;
            }

            // 兜底：聊天框保护或切出游戏时 KeyUp 不会送达，直接查快捷键的物理状态
            if (_holdCheckKey is { } vk
                && Stopwatch.GetElapsedTime(startTimestamp, now).TotalMilliseconds >= HoldCheckGraceMs
                && (User32.GetAsyncKeyState((int)vk) & 0x8000) == 0)
            {
                _keyHeld = false;
                SetStopReason("松开快捷键");
                return false;
            }

            return true;
        }

        try
        {
            var running = true;
            while (running)
            {
                var roundStart = Stopwatch.GetTimestamp();
                foreach (var command in commands)
                {
                    if (!ShouldContinue() || !executor.Execute(command, ct, ShouldContinue))
                    {
                        running = false;
                        break;
                    }
                }

                if (!running || _mode == KeyMacroTriggerMode.Once)
                {
                    break;
                }

                var elapsed = (int)Stopwatch.GetElapsedTime(roundStart).TotalMilliseconds;
                if (elapsed < MinLoopIntervalMs && !PreciseDelay.Wait(MinLoopIntervalMs - elapsed, ct, ShouldContinue))
                {
                    break;
                }

                running = ShouldContinue();
            }
        }
        catch (Exception e)
        {
            SetStopReason("执行出错");
            Logger.LogError(e, "按键宏「{Name}」执行出错：{Message}", _name, e.Message);
        }
        finally
        {
            executor.ReleaseAll(e => Logger.LogDebug(e, "按键宏「{Name}」松开按键失败", _name));
            PreciseDelay.ReleaseThreadTimer();

            string reason;
            lock (_lock)
            {
                reason = _stopReason ?? "执行完成";
                _thread = null;
                _cts?.Dispose();
                _cts = null;
            }

            Logger.LogInformation("按键宏「{Name}」停止：{Reason}", _name, reason);
            RunningChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
