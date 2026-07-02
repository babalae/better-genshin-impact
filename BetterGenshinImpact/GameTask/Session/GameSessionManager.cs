using BetterGenshinImpact.Core.CloudGame;
using BetterGenshinImpact.Core.Profile;
using BetterGenshinImpact.Core.Simulator.Cloud;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Session;

/// <summary>
/// 管理当前进程内的全部游戏会话。
/// 本期负责创建和销毁多个云原神会话，并为每个会话装配独立的 WebView2、截图、输入及任务调度器。
/// </summary>
/// <param name="profileService">提供 Profile 配置和独立 WebView2 UDF 路径。</param>
public sealed class GameSessionManager(ProfileService profileService)
{
    // 以 ProfileId/SessionId 为键保存当前进程内已启动的会话。
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();

    // 记录窗口恢复等基础设施异常。
    private readonly ILogger<GameSessionManager> _logger = App.GetLogger<GameSessionManager>();

    /// <summary>
    /// 获取当前全部会话的线程安全快照。
    /// </summary>
    public IReadOnlyCollection<GameSession> Sessions => _sessions.Values.ToArray();

    /// <summary>
    /// 按 SessionId 获取当前已启动会话。
    /// </summary>
    public GameSession? GetSession(Guid sessionId) => _sessions.GetValueOrDefault(sessionId);

    /// <summary>
    /// 根据 Profile 创建或返回已有的云原神会话。
    /// 每个 Profile 使用独立 WebView2 UDF，因而登录态互不影响。
    /// </summary>
    public async Task<GameSession> CreateCloudSessionAsync(BetterGiProfile profile)
    {
        if (_sessions.TryGetValue(profile.Id, out var existing))
        {
            // 同一 Profile 同一时刻只允许一个 WebView2 会话。
            return existing;
        }

        // 读取云模块配置，通用 Profile 本身不保存云页面专属字段。
        var module = profileService.ReadModuleConfig(profile.Id);

        // 每个 Profile 使用独立 UDF 和独立 STA 宿主。
        var host = new CloudGameHostWindow(
            profile.Name,
            profileService.GetWebView2DataPath(profile.Id),
            module.Url);
        await host.StartAsync();

        // 按 Target、Input、Capture、Session 的依赖顺序装配完整会话。
        var bridge = new CloudGameJsBridge(host);

        // 输入后端通过 bridge 将命令发送给当前页面。
        var input = new CloudInputBackend(bridge);

        // 截图源只绑定当前 host，不共享最新帧。
        var capture = new CloudGameCapture(host);

        // Target 暴露窗口生命周期和显示模式。
        var target = new CloudGameTarget(host);

        // SystemInfo 将现有坐标链绑定到云鼠标和固定 1080P 画面。
        var systemInfo = new CloudGameSystemInfo(input.Mouse, host.BrowserProcessId);

        // Session 聚合所有实例级可变上下文。
        var session = new GameSession(profile, target, input, capture, systemInfo);

        // 每个会话创建独立实时触发器调度器。
        var dispatcher = new TaskTriggerDispatcher(session);
        session.AttachDispatcher(dispatcher);

        // 并发创建同一 Profile 时只保留第一个成功登记的会话。
        if (!_sessions.TryAdd(profile.Id, session))
        {
            // 当前分支创建的 WebView2 不再使用，必须立即释放。
            await session.DisposeAsync();
            return _sessions[profile.Id];
        }

        input.DispatchFailed += (_, e) =>
        {
            // 队列消费者在后台线程触发事件，先进入正确会话再访问静态兼容代理。
            using var scope = GameSessionContext.Enter(session);

            // 分发失败后清除旧输入并取消正在执行的脚本任务，禁止重连重放。
            input.ClearPending();
            session.CancellationContext.Cancel();
            if (e.Message.Contains("ClientCore", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("YSInputInject", StringComparison.OrdinalIgnoreCase))
            {
                // 私有模块或附件脚本失效属于版本不兼容，不回退到系统键鼠。
                session.UpdateState(GameSessionState.Incompatible, $"云输入脚本不兼容：{e.GetBaseException().Message}");
            }
            else
            {
                session.UpdateState(GameSessionState.Disconnected, $"云输入通道异常：{e.GetBaseException().Message}");
            }
        };

        host.NavigationStarted += (_, _) =>
        {
            // 新文档不能继承旧文档的输入意图。
            input.ClearPending();
            session.UpdateState(GameSessionState.WaitingForLogin, "页面正在加载");
        };
        host.ProcessFailed += (_, e) =>
        {
            // 浏览器进程异常由用户关闭并重新创建实例处理。
            session.UpdateState(GameSessionState.Faulted, $"WebView2 进程异常：{e.ProcessFailedKind}");
        };
        capture.RepeatedCaptureFailed += async (_, _) =>
        {
            // 先反映故障，再根据隐藏状态决定是否自动恢复只读窗口。
            session.UpdateState(GameSessionState.Disconnected,
                target.DisplayMode == CloudGameDisplayMode.Hidden
                    ? "隐藏截图连续失败，已恢复只读显示"
                    : "WebView2 连续截图失败");
            if (target.DisplayMode == CloudGameDisplayMode.Hidden)
            {
                try
                {
                    // 不尝试修改 Chromium 参数，只将父 HWND 恢复为只读可见。
                    await target.ShowReadOnlyAsync();
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "恢复云原神只读窗口失败，SessionId={SessionId}", session.Id);
                }
            }
        };

        // 会话创建完成后立即启动帧泵和独立健康检查。
        capture.Start(target.WindowHandle);

        // 健康检查生命周期由 session.LifetimeToken 控制，无需阻塞创建流程。
        _ = MonitorHealthAsync(session, bridge, session.LifetimeToken);
        return session;
    }

    /// <summary>
    /// 从管理器移除并完整释放指定会话。
    /// </summary>
    public async Task StopSessionAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>
    /// 依次停止当前进程中的全部会话，主要用于应用退出。
    /// </summary>
    public async Task StopAllAsync()
    {
        // 对键集合取快照，避免遍历期间 TryRemove 修改字典。
        foreach (var sessionId in _sessions.Keys.ToArray())
        {
            await StopSessionAsync(sessionId);
        }
    }

    /// <summary>
    /// 持续检查页面私有 API 和 RTC 通道。
    /// 隐藏运行期间发生 RTC 断线时，会恢复为只读显示并取消当前任务。
    /// </summary>
    private async Task MonitorHealthAsync(
        GameSession session,
        CloudGameJsBridge bridge,
        CancellationToken cancellationToken)
    {
        // 区分“尚未成功连接”与“曾经 Ready 后断线”，避免登录阶段误报断线。
        var wasReady = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // status() 会同时验证 webpack Runtime、ClientCore 和 RTC 通道。
                var health = await bridge.CheckHealthAsync(cancellationToken);
                using (GameSessionContext.Enter(session))
                {
                    if (health.IsReady)
                    {
                        wasReady = true;
                        if (session.State != GameSessionState.Running)
                        {
                            // 运行中的任务不应被周期健康检查降回 Ready。
                            session.UpdateState(GameSessionState.Ready, "云游戏连接已就绪");
                        }
                    }
                    else if (health.Error?.Contains("ClientCore", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // 模块 53638 失效时终止当前任务并标记不兼容。
                        session.CancellationContext.Cancel();
                        session.UpdateState(GameSessionState.Incompatible, health.Error);
                    }
                    else if (wasReady && health.RtcDataChannelState != "open")
                    {
                        // 只将曾经成功连接后关闭的 DataChannel 判定为真正断线。
                        session.CancellationContext.Cancel();
                        if (session.Target is CloudGameTarget
                            {
                                DisplayMode: CloudGameDisplayMode.Hidden
                            } cloudTarget)
                        {
                            // 隐藏运行断线时主动恢复只读窗口，让用户看到页面状态。
                            await cloudTarget.ShowReadOnlyAsync();
                            session.UpdateState(GameSessionState.Disconnected,
                                $"RTC 通道已断开（{health.RtcDataChannelState ?? "unknown"}），已恢复只读显示");
                        }
                        else
                        {
                            session.UpdateState(GameSessionState.Disconnected,
                                $"RTC 通道已断开（{health.RtcDataChannelState ?? "unknown"}）");
                        }
                    }
                    else
                    {
                        // 首次登录和连接过程中保持 WaitingForLogin，不视为异常。
                        session.UpdateState(GameSessionState.WaitingForLogin,
                            health.Error ?? $"等待云游戏连接（{health.RtcDataChannelState ?? "unknown"}）");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // 页面刷新等瞬时执行脚本错误会在下一轮健康检查继续恢复。
                session.UpdateState(GameSessionState.Disconnected, e.GetBaseException().Message);
            }

            try
            {
                // 一秒一次即可满足连接状态监控，避免频繁执行页面脚本。
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
