using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// Worker 侧出站消息出口：把无头 Worker 里产生的内容推送给所有已授权的 Controller。
/// <para>
/// 目前承载两类消息：无头 Worker 没有主窗口，Toast 既可能抛 <see cref="ArgumentNullException"/>，
/// 即使显示出来也只有 Worker 所在用户能看到，因此提示统一回传；日志行则在
/// <see cref="WorkerLogDisplayMode.LocalWindow"/> 下按批次回传。
/// 没有 Controller 连接时静默丢弃，调用方负责写入本地日志。
/// </para>
/// </summary>
internal sealed class WorkerNoticeHub
{
    private readonly ConcurrentDictionary<InstanceConnection, byte> _controllers = new();
    private readonly ILogger<WorkerNoticeHub> _logger;
    private long _sequence;

    public WorkerNoticeHub(ILogger<WorkerNoticeHub> logger)
    {
        _logger = logger;
    }

    public bool HasControllers => !_controllers.IsEmpty;

    /// <summary>
    /// SID 校验通过后登记，只有已授权的 Controller 才收得到提示
    /// </summary>
    public void Register(InstanceConnection connection)
    {
        _controllers.TryAdd(connection, 0);
    }

    public void Unregister(InstanceConnection connection)
    {
        _controllers.TryRemove(connection, out _);
    }

    /// <summary>
    /// 发布一条提示。任意线程可调用：不写管道、不抛异常，实际发送在后台完成
    /// </summary>
    public void Publish(WorkerNoticeLevel level, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _controllers.IsEmpty)
        {
            return;
        }

        var notice = new WorkerNotice
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Level = level,
            Message = message,
            Timestamp = DateTimeOffset.Now
        };

        _ = Task.Run(() => SendToControllersAsync(InstanceOperations.WorkerNotice, notice));
    }

    /// <summary>
    /// 发布一批日志行（显示位置为「本地独立窗口」时使用）
    /// </summary>
    public async Task PublishLogBatchAsync(string[] lines)
    {
        if (lines.Length == 0 || _controllers.IsEmpty)
        {
            return;
        }

        var batch = new WorkerLogBatch
        {
            Sequence = Interlocked.Increment(ref _sequence),
            Lines = lines
        };

        await SendToControllersAsync(InstanceOperations.WorkerLog, batch).ConfigureAwait(false);
    }

    private async Task SendToControllersAsync(string operation, object payload)
    {
        foreach (var connection in _controllers.Keys)
        {
            try
            {
                await connection.WriteJsonAsync(
                    InstanceIpcEnvelope.Request(operation, payload),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // 管道已断：断开流程会走 ConnectionClosed 正式清理，这里只做兜底并避免刷日志
                _logger.LogDebug(exception, "向 Controller 推送 {Operation} 失败", operation);
                _controllers.TryRemove(connection, out _);
            }
        }
    }
}
