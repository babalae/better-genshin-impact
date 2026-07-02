using Fischless.GameCapture;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// 云原神截图源。
/// 后台帧泵串行调用 WebView2 CapturePreview，并以原子替换方式维护一份最新帧；
/// 任务调度器只克隆该最新帧，不会并发调用 WebView2 截图 API。
/// </summary>
public sealed class CloudGameCapture : IGameCapture
{
    // 当前会话的 WebView2 宿主，唯一允许实际执行 CapturePreview 的对象。
    private readonly CloudGameHostWindow _hostWindow;

    // 保护最新 Mat 的替换、克隆和释放，避免帧泵与任务调度器并发访问。
    private readonly object _frameLock = new();

    // 控制当前帧泵生命周期的取消源。
    private CancellationTokenSource? _cts;

    // 后台串行截图任务；同一实例任何时刻最多存在一个。
    private Task? _framePump;

    // 最近一次成功解码的 BGR 画面，所有权归 CloudGameCapture。
    private Mat? _latestFrame;

    /// <summary>
    /// 创建绑定到指定 WebView2 宿主的截图源。
    /// </summary>
    /// <param name="hostWindow">当前云会话独占的 WebView2 宿主。</param>
    public CloudGameCapture(CloudGameHostWindow hostWindow)
    {
        _hostWindow = hostWindow;
    }

    /// <summary>
    /// 获取截图帧泵是否正在运行。
    /// </summary>
    public bool IsCapturing => _framePump is { IsCompleted: false };

    /// <summary>
    /// 连续三次无法获得有效截图时触发。
    /// </summary>
    public event EventHandler? RepeatedCaptureFailed;

    /// <summary>
    /// 启动截图帧泵。云截图不依赖传入的窗口句柄或 WGC。
    /// </summary>
    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        if (IsCapturing)
        {
            // Start 设计为幂等，防止调度器重复创建并发 CapturePreview 调用。
            return;
        }

        // 截图 API 由唯一后台任务串行调用。
        _cts = new CancellationTokenSource();
        _framePump = Task.Run(() => PumpFramesAsync(_cts.Token));
    }

    /// <summary>
    /// 返回最新成功截图的独立副本；尚未得到首帧时返回 <see langword="null"/>。
    /// </summary>
    public GameCaptureFrame? Capture()
    {
        lock (_frameLock)
        {
            // 返回克隆而不是内部 Mat，调用方可以独立释放 GameCaptureFrame。
            return _latestFrame == null ? null : new GameCaptureFrame(_latestFrame.Clone());
        }
    }

    /// <summary>
    /// 停止帧泵并释放缓存的最新画面。
    /// </summary>
    public void Stop()
    {
        // 先请求取消，再短暂等待帧泵退出，避免在释放 Mat 后继续写入。
        _cts?.Cancel();
        try
        {
            _framePump?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 停止截图时忽略正在退出的 WebView 异常。
        }
        // 无论帧泵是否已正常结束，后续都不再复用这一轮运行对象。
        _cts?.Dispose();
        _cts = null;
        _framePump = null;
        lock (_frameLock)
        {
            // 内部 Mat 由截图源持有，停止时必须显式释放原生内存。
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    /// <summary>
    /// 串行捕获页面并替换最新帧，连续失败三次时通知会话恢复只读窗口。
    /// </summary>
    private async Task PumpFramesAsync(CancellationToken cancellationToken)
    {
        // 失败计数只统计连续失败，任意成功帧都会归零。
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // CapturePreview 返回 PNG 流；当前循环完成前即释放。
                await using var stream = await _hostWindow.CapturePreviewAsync(cancellationToken);

                // 将 PNG 流复制为连续字节，供 OpenCV 解码。
                var bytes = stream.ToArray();

                // 统一解码为 OpenCV BGR Mat，与现有识图链保持一致。
                using var frame = Cv2.ImDecode(bytes, ImreadModes.Color);
                if (frame.Empty())
                {
                    throw new InvalidOperationException("WebView2 返回了空截图");
                }

                lock (_frameLock)
                {
                    // 在同一临界区内释放旧帧并替换克隆，保证读取方始终看到完整 Mat。
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                }
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                consecutiveFailures++;
                if (consecutiveFailures == 3)
                {
                    // 每轮连续故障只在第三次通知一次，避免持续弹出恢复提示。
                    RepeatedCaptureFailed?.Invoke(this, EventArgs.Empty);
                }

                // 故障期间降低重试频率，防止 WebView2 异常时形成紧密循环。
                await Task.Delay(250, cancellationToken);
            }

            // 正常帧率约为 20 FPS，任务调度器按自身频率读取最新帧。
            await Task.Delay(50, cancellationToken);
        }
    }

    /// <summary>
    /// 同步释放截图源。
    /// </summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
