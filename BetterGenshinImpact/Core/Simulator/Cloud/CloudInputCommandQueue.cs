using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Simulator.Cloud;

/// <summary>
/// 云输入的单消费者有序命令队列。
/// 多个任务线程可以并发入队，但只有一个消费者会批量调用 JavaScript 桥，
/// 从而保证按下、移动、释放等操作的执行顺序。
/// </summary>
public sealed class CloudInputCommandQueue : IAsyncDisposable
{
    /// <summary>
    /// 队列内部元素：普通输入命令或用于 Flush 的完成屏障。
    /// </summary>
    private sealed record QueueItem(CloudInputCommand? Command, TaskCompletionSource? Barrier);

    // 最终接收有序命令批次的页面 JavaScript 桥。
    private readonly ICloudJsBridge _bridge;

    // 多写单读无界 Channel；输入顺序由 Channel 和唯一消费者共同保证。
    private readonly Channel<QueueItem> _channel = Channel.CreateUnbounded<QueueItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    // 控制消费者任务生命周期。
    private readonly CancellationTokenSource _cts = new();

    // 从构造开始持续运行的唯一消费者任务。
    private readonly Task _worker;

    /// <summary>
    /// 任一命令批次无法发送到页面时触发。
    /// </summary>
    public event EventHandler<Exception>? DispatchFailed;

    /// <summary>
    /// 创建命令队列并立即启动唯一消费者。
    /// </summary>
    /// <param name="bridge">当前云页面的 JavaScript 桥。</param>
    public CloudInputCommandQueue(ICloudJsBridge bridge)
    {
        _bridge = bridge;

        // ProcessAsync 由后台任务独占读取 Channel。
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>
    /// 将命令加入队尾。队列停止后继续写入会直接抛出异常。
    /// </summary>
    public void Enqueue(CloudInputCommand command)
    {
        if (!_channel.Writer.TryWrite(new QueueItem(command, null)))
        {
            throw new InvalidOperationException("云原神输入队列已经停止");
        }
    }

    /// <summary>
    /// 插入同步屏障并等待屏障前的命令全部提交至页面。
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // 屏障自身不包含输入；消费者处理完此前批次后设置完成状态。
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new QueueItem(null, completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 丢弃尚未被消费者取出的命令，并取消对应的同步屏障。
    /// </summary>
    public void Clear()
    {
        while (_channel.Reader.TryRead(out var item))
        {
            // 被清空的 Flush 不能无限等待，显式将对应屏障标记为取消。
            item.Barrier?.TrySetCanceled();
        }
    }

    /// <summary>
    /// 单消费者循环。命令会按读取顺序组成小批次后提交给 JavaScript 桥。
    /// </summary>
    private async Task ProcessAsync()
    {
        // 复用批次列表，减少高频键鼠输入造成的短期对象分配。
        var batch = new List<CloudInputCommand>(64);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    if (item.Command != null)
                    {
                        batch.Add(item.Command);
                    }

                    if (item.Barrier != null || batch.Count >= 128)
                    {
                        // Flush 屏障或批次上限都会强制立即提交当前批次。
                        await DispatchBatchAsync(batch, _cts.Token);
                        item.Barrier?.TrySetResult();
                    }
                }

                // 当前可读队列排空后提交尾部不足 128 条的命令。
                await DispatchBatchAsync(batch, _cts.Token);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // 消费者级异常会结束当前工作任务，由会话管理器处理断线状态。
            DispatchFailed?.Invoke(this, e);
        }
    }

    /// <summary>
    /// 将当前批次复制为稳定数组后提交给页面，并清空复用列表。
    /// </summary>
    private async Task DispatchBatchAsync(List<CloudInputCommand> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        // 在 await 前复制批次，防止复用列表后续修改影响正在分发的数据。
        var commands = batch.ToArray();
        batch.Clear();
        try
        {
            await _bridge.DispatchAsync(commands, cancellationToken);
        }
        catch (Exception e)
        {
            // 单批失败不终止消费者，后续健康检查和会话状态决定是否继续。
            DispatchFailed?.Invoke(this, e);
        }
    }

    /// <summary>
    /// 停止接收命令、取消消费者并等待后台任务退出。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 先拒绝新写入，再取消可能正在等待数据的消费者。
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }
}
