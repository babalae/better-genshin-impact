using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// Worker 日志出口：把原本写入遮罩叠加层日志框的日志行按 <see cref="WorkerLogDisplayMode"/>
/// 路由到选定的显示位置。
/// <para>
/// 只对 Worker（<c>--headless</c>）生效。普通实例的日志照旧由遮罩日志框 sink 处理，
/// 本类既不改变也不拦截它们的输出。
/// </para>
/// </summary>
public static class WorkerLogRouter
{
    /// <summary>与遮罩日志框完全一致的输出格式，保证「原封不动」</summary>
    private const string LineTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>走通知渠道时的默认聚合间隔（秒）</summary>
    public const int DefaultNotificationIntervalSeconds = 5;

    /// <summary>回传控制端的批次上限，避免小批次高频占用管道</summary>
    private const int ForwardMaxLines = 50;
    private static readonly TimeSpan ForwardInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>独立窗口的刷新间隔，比回传更短以保证观感接近实时</summary>
    private const int WindowMaxLines = 50;
    private static readonly TimeSpan WindowInterval = TimeSpan.FromMilliseconds(200);

    private static readonly object Gate = new();
    private static readonly MessageTemplateTextFormatter Formatter =
        new(LineTemplate, CultureInfo.InvariantCulture);

    private static WorkerLogDisplayMode _mode = WorkerLogDisplayMode.GameOverlay;
    private static int _notificationIntervalSeconds = DefaultNotificationIntervalSeconds;
    private static int _notificationBatcherIntervalSeconds;
    private static WorkerLogBatcher? _notificationBatcher;
    private static WorkerLogBatcher? _windowBatcher;
    private static WorkerLogBatcher? _forwardBatcher;

    /// <summary>
    /// 挂到 Serilog 配置上的日志 sink
    /// </summary>
    public static ILogEventSink Sink { get; } = new RouterSink();

    /// <summary>当前生效的日志显示位置</summary>
    public static WorkerLogDisplayMode Mode
    {
        get
        {
            lock (Gate)
            {
                return _mode;
            }
        }
    }

    /// <summary>当前生效的通知聚合间隔（秒）</summary>
    public static int NotificationIntervalSeconds
    {
        get
        {
            lock (Gate)
            {
                return _notificationIntervalSeconds;
            }
        }
    }

    /// <summary>
    /// 遮罩叠加层日志框是否继续接收日志。非 Worker 恒为 true，因此普通实例行为与改动前一致
    /// </summary>
    public static bool ShouldWriteToGameOverlay =>
        !CommandLineOptions.Instance.Headless || Mode == WorkerLogDisplayMode.GameOverlay;

    /// <summary>
    /// 应用显示位置。可由 Controller 通过 <c>worker.logMode</c> 在运行中修改；
    /// 切换前会把旧位置尚未发送的日志行冲刷出去，避免用户刚改设置就丢日志
    /// </summary>
    public static void Configure(WorkerLogDisplayMode mode, int notificationIntervalSeconds)
    {
        if (!Enum.IsDefined(mode))
        {
            mode = WorkerLogDisplayMode.GameOverlay;
        }

        WorkerLogBatcher? previousBatcher;
        lock (Gate)
        {
            previousBatcher = mode == _mode ? null : PeekBatcher(_mode);
            _mode = mode;
            _notificationIntervalSeconds = notificationIntervalSeconds > 0
                ? notificationIntervalSeconds
                : DefaultNotificationIntervalSeconds;
        }

        if (previousBatcher is not null)
        {
            _ = previousBatcher.FlushAsync();
        }
    }

    /// <summary>
    /// 处理一条日志事件。任意线程可调用：绝不向外抛异常，否则会打断 Serilog 日志管线
    /// </summary>
    private static void Dispatch(LogEvent logEvent)
    {
        try
        {
            if (!CommandLineOptions.Instance.Headless)
            {
                return;
            }

            var destination = ResolveDestination(Mode);
            if (destination is null)
            {
                // 叠加层由原有 sink 负责；不显示则什么都不做
                return;
            }

            BatcherFor(destination.Value)?.Add(FormatLine(logEvent));
        }
        catch (Exception)
        {
            // 日志出口失败不能影响业务代码的日志调用
        }
    }

    /// <summary>
    /// 显示位置对应的日志去向；返回 null 表示本类不参与该位置的输出
    /// </summary>
    internal static WorkerLogDestination? ResolveDestination(WorkerLogDisplayMode mode)
    {
        return mode switch
        {
            WorkerLogDisplayMode.Notification => WorkerLogDestination.NotificationChannel,
            WorkerLogDisplayMode.RemoteWindow => WorkerLogDestination.WorkerWindow,
            WorkerLogDisplayMode.LocalWindow => WorkerLogDestination.ControllerWindow,
            _ => null
        };
    }

    /// <summary>
    /// 渲染成与遮罩日志框完全一致的一行文本（模板见 <see cref="LineTemplate"/>）
    /// </summary>
    internal static string FormatLine(LogEvent logEvent)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Formatter.Format(logEvent, writer);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// 取指定显示位置已经存在的批次发送器，不创建
    /// </summary>
    private static WorkerLogBatcher? PeekBatcher(WorkerLogDisplayMode mode)
    {
        return ResolveDestination(mode) switch
        {
            WorkerLogDestination.NotificationChannel => _notificationBatcher,
            WorkerLogDestination.WorkerWindow => _windowBatcher,
            WorkerLogDestination.ControllerWindow => _forwardBatcher,
            _ => null
        };
    }

    /// <summary>
    /// 取（必要时创建）指定去向对应的批次发送器
    /// </summary>
    private static WorkerLogBatcher? BatcherFor(WorkerLogDestination destination)
    {
        switch (destination)
        {
            case WorkerLogDestination.NotificationChannel:
                lock (Gate)
                {
                    // 间隔由 Controller 控制，改动后需要换掉定时器
                    if (_notificationBatcher is null
                        || _notificationBatcherIntervalSeconds != _notificationIntervalSeconds)
                    {
                        _notificationBatcher?.Dispose();
                        _notificationBatcherIntervalSeconds = _notificationIntervalSeconds;
                        _notificationBatcher = new WorkerLogBatcher(
                            TimeSpan.FromSeconds(_notificationIntervalSeconds),
                            int.MaxValue,
                            SendNotificationAsync);
                    }

                    return _notificationBatcher;
                }

            case WorkerLogDestination.WorkerWindow:
                lock (Gate)
                {
                    return _windowBatcher ??= new WorkerLogBatcher(WindowInterval, WindowMaxLines, ShowInWindowAsync);
                }

            case WorkerLogDestination.ControllerWindow:
                lock (Gate)
                {
                    return _forwardBatcher ??=
                        new WorkerLogBatcher(ForwardInterval, ForwardMaxLines, ForwardToControllerAsync);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// 走通知渠道：把聚合间隔内的日志合并成一条通知
    /// </summary>
    private static Task SendNotificationAsync(IReadOnlyList<string> lines)
    {
        var message = WorkerLogNotificationFormatter.Format(lines);
        if (message is null)
        {
            return Task.CompletedTask;
        }

        // Send() 内部异步发送并吞掉异常，这里是通知渠道唯一的入口
        new BaseNotificationData
        {
            Event = NotificationEvent.WorkerLog.Code,
            Result = NotificationEventResult.Success,
            Message = message
        }.Send();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 远程独立窗口：在 Worker 侧弹出窗口显示
    /// </summary>
    private static Task ShowInWindowAsync(IReadOnlyList<string> lines)
    {
        try
        {
            App.GetService<WorkerLogWindowService>()?.Append(lines);
        }
        catch (Exception)
        {
            // 窗口创建失败（例如没有 UI 线程）时静默降级：日志已经在文件里
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 本地独立窗口：把日志行回传给 Controller
    /// </summary>
    private static async Task ForwardToControllerAsync(IReadOnlyList<string> lines)
    {
        var hub = App.GetService<WorkerNoticeHub>();
        if (hub is null)
        {
            return;
        }

        await hub.PublishLogBatchAsync([.. lines]).ConfigureAwait(false);
    }

    /// <summary>
    /// 供测试与诊断使用：立即冲刷所有待发送的日志行
    /// </summary>
    internal static void FlushPending()
    {
        lock (Gate)
        {
            _ = _notificationBatcher?.FlushAsync();
            _ = _windowBatcher?.FlushAsync();
            _ = _forwardBatcher?.FlushAsync();
        }
    }

    private sealed class RouterSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            Dispatch(logEvent);
        }
    }
}

/// <summary>
/// Worker 日志的去向。用于把「显示位置 → 输出通道」的映射从路由实现中分离出来，便于验证
/// </summary>
public enum WorkerLogDestination
{
    /// <summary>通知渠道（Worker 侧发送）</summary>
    NotificationChannel,

    /// <summary>Worker 侧的独立窗口</summary>
    WorkerWindow,

    /// <summary>回传控制端，由控制侧的独立窗口显示</summary>
    ControllerWindow
}
